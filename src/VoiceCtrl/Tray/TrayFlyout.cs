using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Core.Tray;
using VoiceCtrl.Interop;
using VoiceCtrl.Overlay;
using VoiceCtrl.Web;

namespace VoiceCtrl.Tray;

/// <summary>
/// The tray flyout (DESIGN §12, spec §2 and §7): VoiceCtrl's own menu by the tray icon, in a
/// transparent popup that can take focus, has no taskbar button and hides when it loses focus.
/// It is created once and kept warm, hidden between uses, so it opens at once. An opening goes:
/// <c>tray.open</c> to the page, the page lays out and answers <c>tray.place</c> with its size,
/// and only then is the window put by the icon (<see cref="TrayFlyoutPlacement"/>) and shown.
/// </summary>
internal sealed class TrayFlyout : IDisposable
{
    /// <summary>A right-click on the icon that lands just after the flyout lost focus is the click
    /// that closed it, not a request to open it again.</summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    /// <summary>While the page is still loading it can't hear <c>tray.open</c> yet, so an opening
    /// is repeated this often until the page answers, for at most <see cref="OpenRetries"/> tries.</summary>
    private const int OpenRetryMs = 150;
    private const int OpenRetries = 40;

    private readonly SynchronizationContext _ui;
    private readonly Func<(TranscriptionModePreference Mode, bool HasLatest)> _state;
    private readonly FlyoutWindow _window;
    private readonly WebSurface _surface;
    private readonly System.Windows.Forms.Timer _openRetry = new() { Interval = OpenRetryMs };
    private PixelRect? _icon;
    private TrayOpening? _opening;
    private int _retriesLeft;
    private bool _shown;
    private long _hiddenAt;

    /// <param name="registerCommands">The <c>app.*</c> and <c>settings.setMode</c> commands the
    /// page shares with the Hub.</param>
    /// <param name="state">Mode and whether there is a latest Transcription, read at each opening.</param>
    public TrayFlyout(WebViewRuntime runtime, SynchronizationContext ui, Action<BridgeDispatcher> registerCommands, Func<(TranscriptionModePreference Mode, bool HasLatest)> state)
    {
        _ui = ui;
        _state = state;
        _window = new FlyoutWindow(this);
        _surface = new WebSurface(runtime, _window.Handle, "flyout.html", dispatcher =>
        {
            registerCommands(dispatcher);
            RegisterTrayCommands(dispatcher);
        }, transparent: true);
        _openRetry.Tick += (_, _) => RepeatOpening();
    }

    public event Action? HubRequested;

    public event Action? SettingsRequested;

    public event Action? CopyLatestRequested;

    public event Action? QuitRequested;

    public bool IsOpen => _shown;

    public async Task StartAsync()
    {
        try
        {
            await _surface.StartAsync();
            _surface.SetVisible(false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            SimpleFileLogger.LogError("WebView2 start (flyout.html)", ex);
        }
    }

    /// <summary>A right-click on the icon (<paramref name="icon"/> in physical pixels): opens the
    /// flyout, or closes it if it is open.</summary>
    public void Toggle(PixelRect icon)
    {
        if (_shown)
        {
            Hide();
            return;
        }

        if (Stopwatch.GetElapsedTime(_hiddenAt) < ReopenGuard)
        {
            return;
        }

        _icon = icon;
        PixelRect workArea = Describe(icon)?.WorkArea ?? icon;
        (TranscriptionModePreference mode, bool hasLatest) = _state();
        _opening = new TrayOpening(TrayFlyoutPlacement.Corner(icon, workArea), mode, hasLatest);
        _retriesLeft = OpenRetries;
        _surface.Post(_opening);
        _openRetry.Start();
    }

    public void Hide()
    {
        _openRetry.Stop();
        _opening = null;
        if (!_shown)
        {
            return;
        }

        _shown = false;
        _hiddenAt = Stopwatch.GetTimestamp();
        _surface.SetVisible(false);
        Win32.ShowWindow(_window.Handle, Win32.SW_HIDE);
    }

    private void RepeatOpening()
    {
        if (_opening is null || --_retriesLeft <= 0)
        {
            _openRetry.Stop();
            return;
        }

        _surface.Post(_opening);
    }

    private void RegisterTrayCommands(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<PlaceTrayFlyout>(args =>
        {
            Place(args.Width, args.Height);
            return Task.CompletedTask;
        });
        dispatcher.Handle<CloseTrayFlyout>(_ =>
        {
            Hide();
            return Task.CompletedTask;
        });

        // Each pick is done after the reply goes out: Quit and the Hub take the UI thread for a while.
        dispatcher.Handle<OpenHubFromTray>(_ => After(HubRequested));
        dispatcher.Handle<OpenSettingsFromTray>(_ => After(SettingsRequested));
        dispatcher.Handle<CopyLatestFromTray>(_ => After(CopyLatestRequested));
        dispatcher.Handle<QuitFromTray>(_ => After(QuitRequested));
    }

    private Task After(Action? pick)
    {
        Hide();
        _ui.Post(_ => pick?.Invoke(), null);
        return Task.CompletedTask;
    }

    /// <summary>Puts the window by the icon for a menu this size (CSS pixels) and shows it, or
    /// moves it if it is already showing and the menu changed height.</summary>
    private void Place(double widthDip, double heightDip)
    {
        if (_icon is not { } icon || (_opening is null && !_shown)
            || !double.IsFinite(widthDip) || !double.IsFinite(heightDip)
            || widthDip is < 1 or > 2000 || heightDip is < 1 or > 2000)
        {
            return;
        }

        _openRetry.Stop();
        _opening = null;
        (PixelRect workArea, double dpiScale) = Describe(icon) ?? (icon, 1.0);
        PixelRect window = TrayFlyoutPlacement.Window(TrayFlyoutPlacement.Menu(icon, workArea, dpiScale, widthDip, heightDip), dpiScale);
        _surface.SetBounds(new Rectangle(0, 0, window.Width, window.Height));

        if (_shown)
        {
            NativeMethods.SetWindowPos(_window.Handle, Win32.HWND_TOPMOST, window.Left, window.Top, window.Width, window.Height, Win32.SWP_NOACTIVATE);
            return;
        }

        NativeMethods.SetWindowPos(_window.Handle, Win32.HWND_TOPMOST, window.Left, window.Top, window.Width, window.Height, Win32.SWP_SHOWWINDOW);
        _surface.SetVisible(true);
        _shown = true;

        // The shell lets the app that owns a clicked tray icon take the foreground, so the flyout
        // gets the keyboard straight away (and loses it, and hides, on a click anywhere else).
        Win32.SetForegroundWindow(_window.Handle);
        _surface.Focus();
    }

    private static (PixelRect WorkArea, double DpiScale)? Describe(PixelRect icon) =>
        MonitorPositioner.Describe(MonitorPositioner.FindMonitorAt(icon.Left + (icon.Width / 2), icon.Top + (icon.Height / 2)));

    private void OnDeactivated()
    {
        if (_shown)
        {
            Hide();
        }
    }

    public void Dispose()
    {
        _openRetry.Dispose();
        _surface.Dispose();
        _window.DestroyHandle();
    }

    /// <summary>The raw popup: no redirection bitmap, so WebView2's transparent output is the
    /// only pixels, as on the Overlay; but unlike the Overlay it takes clicks and focus.</summary>
    private sealed class FlyoutWindow : NativeWindow
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

        private readonly TrayFlyout _owner;

        public FlyoutWindow(TrayFlyout owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams
            {
                Caption = "VoiceCtrl",
                Style = WS_POPUP,
                ExStyle = WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_ACTIVATE && (m.WParam.ToInt64() & 0xFFFF) == Win32.WA_INACTIVE)
            {
                _owner.OnDeactivated();
            }

            base.WndProc(ref m);
        }
    }
}
