using Microsoft.Web.WebView2.Core;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Web;

/// <summary>
/// The one WebView2 environment every surface shares (spec §3), with its user data folder under
/// the data root. Knows where pages come from (the <c>ui</c> folder next to the exe, served on a
/// reserved virtual host, or the Vite dev server in a Debug build) and recovers from a lost
/// browser process by building a new environment and re-creating every open surface.
/// </summary>
internal sealed class WebViewRuntime
{
    public const string VirtualHost = "ui.voicectrl.example";

    /// <summary>The message shown when no environment can be created (spec §3, exact copy).</summary>
    public const string MissingRuntimeMessage =
        "VoiceCtrl needs Microsoft Edge WebView2. Connect to the internet and start VoiceCtrl again.";

    private readonly List<WebSurface> _surfaces = [];
    private CoreWebView2Environment _environment;
    private bool _environmentStale;
    private Task? _rebuild;

    private WebViewRuntime(CoreWebView2Environment environment, SurfaceAppearance appearance, Uri pagesBase)
    {
        _environment = environment;
        Appearance = appearance;
        PagesBase = pagesBase;
        AppOrigin = new Uri(pagesBase.GetLeftPart(UriPartial.Authority) + "/");
        _environment.BrowserProcessExited += OnBrowserProcessExited;
    }

    public SurfaceAppearance Appearance { get; }

    /// <summary>Where pages are served from; every page URL is relative to it.</summary>
    public Uri PagesBase { get; }

    /// <summary>The only origin pages may navigate within and the bridge accepts messages from.</summary>
    public Uri AppOrigin { get; }

    /// <summary>The folder mapped to <see cref="VirtualHost"/>, or null when pages come from the dev server.</summary>
    public string? UiFolder { get; private init; }

    public static bool IsRuntimeInstalled()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Creates the environment. Throws if WebView2 can't start at all.</summary>
    public static async Task<WebViewRuntime> CreateAsync(SurfaceAppearance appearance)
    {
        CoreWebView2Environment environment = await CreateEnvironmentAsync();
        if (DevServerUrl() is { } devServer)
        {
            SimpleFileLogger.LogInfo($"WebView2: pages from the dev server {devServer}");
            return new WebViewRuntime(environment, appearance, devServer);
        }

        return new WebViewRuntime(environment, appearance, new Uri($"https://{VirtualHost}/"))
        {
            UiFolder = Path.Combine(AppContext.BaseDirectory, "ui"),
        };
    }

    public Uri PageUrl(string page) => new(PagesBase, page);

    /// <summary>A controller for a surface's window, on a fresh environment if the browser
    /// process has gone away since the last one (it exits once no surface is open).</summary>
    internal async Task<CoreWebView2Controller> CreateControllerAsync(IntPtr parentWindow)
    {
        if (_rebuild is not null)
        {
            await _rebuild;
        }

        if (_environmentStale)
        {
            await ReplaceEnvironmentAsync();
        }

        return await _environment.CreateCoreWebView2ControllerAsync(parentWindow);
    }

    internal void Register(WebSurface surface) => _surfaces.Add(surface);

    /// <summary>Sends an app-wide <c>[BridgeEvent]</c> to every open surface. UI thread only.</summary>
    public void Broadcast<TEvent>(TEvent payload)
        where TEvent : class
    {
        foreach (WebSurface surface in _surfaces)
        {
            surface.Post(payload);
        }
    }

    internal void Unregister(WebSurface surface) => _surfaces.Remove(surface);

    // Nothing is sent from VoiceCtrl (spec §10), and that has to include the browser engine under
    // its pages. Left alone, the WebView2 runtime phones Microsoft by itself: its experiment config
    // (config.edge.skype.com), SmartScreen checks of every page it opens, and component and
    // domain-reliability pings. VED-49 captured those in a network log; these switches turn them
    // off. Our pages come from the exe folder and need none of it.
    internal const string QuietBrowserArguments =
        "--disable-background-networking --disable-component-update --disable-domain-reliability " +
        "--disable-client-side-phishing-detection --disable-sync --no-pings " +
        "--disable-features=msSmartScreenProtection,msEdgeSmartScreenCheck";

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(UserDataPaths.WebView2);
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = QuietBrowserArguments,

            // Crashes stay on this PC; the app doesn't upload them either (Copy diagnostics instead).
            IsCustomCrashReportingEnabled = true,
        };
        return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: UserDataPaths.WebView2, options);
    }

    private static Uri? DevServerUrl()
    {
#if DEBUG
        string? url = Environment.GetEnvironmentVariable("VOICECTRL_UI_DEV_URL");
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? new Uri(uri.GetLeftPart(UriPartial.Authority) + "/") : null;
#else
        return null;
#endif
    }

    private async Task ReplaceEnvironmentAsync()
    {
        _environment.BrowserProcessExited -= OnBrowserProcessExited;
        _environment = await CreateEnvironmentAsync();
        _environment.BrowserProcessExited += OnBrowserProcessExited;
        _environmentStale = false;
    }

    // Raised on the UI thread (the environment was created there).
    private void OnBrowserProcessExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e)
    {
        _environmentStale = true;
        if (e.BrowserProcessExitKind == CoreWebView2BrowserProcessExitKind.Failed && _surfaces.Count > 0)
        {
            SimpleFileLogger.LogInfo($"WebView2: browser process failed; rebuilding {_surfaces.Count} surface(s)");
            _rebuild = RebuildAsync();
        }
    }

    /// <summary>A browser-process loss takes every surface's page with it: re-create them all on a
    /// new environment, without a message (spec §3).</summary>
    private async Task RebuildAsync()
    {
        try
        {
            await ReplaceEnvironmentAsync();
            foreach (WebSurface surface in _surfaces.ToList())
            {
                await surface.RecreateAsync(_environment);
            }
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("WebView2 rebuild", ex);
        }
        finally
        {
            _rebuild = null;
        }
    }
}
