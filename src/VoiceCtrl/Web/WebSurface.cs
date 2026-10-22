using System.Diagnostics;
using System.Drawing;
using Microsoft.Web.WebView2.Core;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Web;

/// <summary>
/// One page in one window (spec §3–4): a WebView2 controller on a host window, locked down,
/// serving one page from the app origin, with its own <see cref="BridgeDispatcher"/> so the page
/// reaches only the commands registered for it. Reloads itself when its renderer dies; the
/// runtime re-creates it when the browser process dies. The Overlay uses it on its raw window, so
/// nothing here assumes a Form.
/// </summary>
internal sealed class WebSurface : IDisposable
{
    // Links a page may open, in the default browser (spec §3). Everything else is dropped.
    private static readonly string[] ExternalHosts = ["github.com", "aistudio.google.com", "typesafe.ai", "build.nvidia.com", "platform.openai.com"];

    private readonly WebViewRuntime _runtime;
    private readonly IntPtr _parentWindow;
    private readonly string _page;
    private readonly BridgeDispatcher _dispatcher;
    private readonly bool _transparent;
    private CoreWebView2Controller? _controller;
    private string? _documentScriptId;
    private Rectangle _bounds;
    private bool _disposed;

    /// <param name="page">The page file, e.g. <c>hub.html</c>.</param>
    /// <param name="registerCommands">Registers every command this surface's page may call.</param>
    /// <param name="transparent">Draws the page over a transparent background instead of the
    /// Theme's ground: the Overlay, whose window shows only the pill (research 15, rule 1).</param>
    public WebSurface(WebViewRuntime runtime, IntPtr parentWindow, string page, Action<BridgeDispatcher> registerCommands, bool transparent = false)
    {
        _runtime = runtime;
        _parentWindow = parentWindow;
        _page = page;
        _transparent = transparent;
        _dispatcher = new BridgeDispatcher(runtime.AppOrigin);
        registerCommands(_dispatcher);
        _runtime.Register(this);
        _runtime.Appearance.Changed += OnAppearanceChanged;
    }

    /// <summary>Raised once the page's first navigation has completed (successfully or not).</summary>
    public event Action? PageLoaded;

    /// <summary>True while a controller is attached: false before the first start finishes and
    /// while a browser-process loss is being recovered.</summary>
    public bool IsAttached => _controller is not null;

    private Color Background => _transparent ? Color.Transparent : _runtime.Appearance.Background;

    public async Task StartAsync()
    {
        CoreWebView2Controller controller = await _runtime.CreateControllerAsync(_parentWindow);
        await AttachAsync(controller);
    }

    /// <summary>After a browser-process loss: a new controller on the new environment, same page.</summary>
    internal async Task RecreateAsync(CoreWebView2Environment environment)
    {
        CloseController();
        await AttachAsync(await environment.CreateCoreWebView2ControllerAsync(_parentWindow));
    }

    public void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        if (_controller is not null)
        {
            _controller.Bounds = bounds;
        }
    }

    /// <summary>Tells WebView2 the window moved, so popups and IME windows follow it.</summary>
    public void NotifyParentMoved() => _controller?.NotifyParentWindowPositionChanged();

    public void Focus() => _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);

    /// <summary>Stops or resumes drawing, for a window that hides between uses (the tray
    /// flyout): hidden, the page keeps running but presents nothing, so the next show draws the
    /// page as it is then rather than the last frame from before.</summary>
    public void SetVisible(bool visible)
    {
        if (_controller is not null)
        {
            _controller.IsVisible = visible;
        }
    }

    /// <summary>Sends a <c>[BridgeEvent]</c> to the page. Dropped while the page isn't there.</summary>
    public void Post<TEvent>(TEvent payload)
        where TEvent : class
    {
        _controller?.CoreWebView2.PostWebMessageAsJson(BridgeDispatcher.EventMessage(payload));
    }

    private async Task AttachAsync(CoreWebView2Controller controller)
    {
        if (_disposed)
        {
            controller.Close();
            return;
        }

        _controller = controller;
        controller.DefaultBackgroundColor = Background;
        controller.Bounds = _bounds;

        CoreWebView2 web = controller.CoreWebView2;
        LockDown(web.Settings);
        if (_runtime.UiFolder is { } folder)
        {
            web.SetVirtualHostNameToFolderMapping(WebViewRuntime.VirtualHost, folder, CoreWebView2HostResourceAccessKind.Deny);
        }

        web.NavigationStarting += OnNavigationStarting;
        web.NewWindowRequested += OnNewWindowRequested;
        web.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        web.WebMessageReceived += OnWebMessageReceived;
        web.ProcessFailed += OnProcessFailed;
        web.NavigationCompleted += OnFirstNavigationCompleted;

        await UpdateDocumentScriptAsync(web);
        controller.IsVisible = true;
        web.Navigate(_runtime.PageUrl(_page).AbsoluteUri);
    }

    private static void LockDown(CoreWebView2Settings settings)
    {
#if DEBUG
        const bool developer = true;
#else
        const bool developer = false;
#endif
        settings.AreDevToolsEnabled = developer;
        settings.AreDefaultContextMenusEnabled = developer;
        settings.AreBrowserAcceleratorKeysEnabled = developer;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsBuiltInErrorPageEnabled = developer;

        // No SmartScreen lookups of our own pages (nothing is sent from the app).
        settings.IsReputationCheckingRequired = false;

        // `app-region: drag` in CSS moves the window, like a title bar (spec §2).
        settings.IsNonClientRegionSupportEnabled = true;
    }

    /// <summary>Keeps the pre-paint script current, so a reload or the next navigation starts in
    /// the right Theme with no flash.</summary>
    private async Task UpdateDocumentScriptAsync(CoreWebView2 web)
    {
        if (_documentScriptId is not null)
        {
            web.RemoveScriptToExecuteOnDocumentCreated(_documentScriptId);
        }

        _documentScriptId = await web.AddScriptToExecuteOnDocumentCreatedAsync(_runtime.Appearance.DocumentCreatedScript);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsAppUrl(e.Uri))
        {
            SimpleFileLogger.LogInfo($"WebView2: blocked navigation away from the app ({_page})");
            e.Cancel = true;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && ExternalHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // async void is the event's shape; DispatchAsync never throws, and posting is guarded.
        string? reply = await _dispatcher.DispatchAsync(e.Source, e.WebMessageAsJson);
        if (reply is null)
        {
            SimpleFileLogger.LogInfo($"Bridge: ignored a message on {_page}");
            return;
        }

        try
        {
            _controller?.CoreWebView2.PostWebMessageAsJson(reply);
        }
        catch (InvalidOperationException)
        {
            // The page went away (closed or crashed) while the command ran.
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            // The page is gone or hung; a reload brings it back and it asks for a new snapshot.
            SimpleFileLogger.LogInfo($"WebView2: renderer {e.ProcessFailedKind} on {_page}; reloading");
            _controller?.CoreWebView2.Reload();
        }

        // A browser-process loss is the runtime's to handle: it rebuilds every surface.
    }

    private void OnFirstNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        ((CoreWebView2)sender!).NavigationCompleted -= OnFirstNavigationCompleted;
        if (!e.IsSuccess)
        {
            SimpleFileLogger.LogInfo($"WebView2: {_page} failed to load ({e.WebErrorStatus})");
        }

        PageLoaded?.Invoke();
    }

    private async void OnAppearanceChanged()
    {
        if (_controller is not { } controller)
        {
            return;
        }

        try
        {
            controller.DefaultBackgroundColor = Background;
            Post(_runtime.Appearance.ThemeEvent);
            Post(new ReducedMotionChanged(_runtime.Appearance.ReducedMotion));
            await UpdateDocumentScriptAsync(controller.CoreWebView2);
        }
        catch (InvalidOperationException)
        {
            // Closed mid-update.
        }
    }

    private bool IsAppUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && Uri.Compare(uri, _runtime.AppOrigin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private void CloseController()
    {
        _controller?.Close();
        _controller = null;
        _documentScriptId = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.Appearance.Changed -= OnAppearanceChanged;
        _runtime.Unregister(this);
        CloseController();
    }
}
