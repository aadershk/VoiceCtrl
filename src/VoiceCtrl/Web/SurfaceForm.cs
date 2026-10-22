using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Web;

/// <summary>A page and its window's size rules, in DIPs (DESIGN §5).</summary>
internal sealed record SurfacePage(string File, Size Size, Size? MinimumSize)
{
    public static readonly SurfacePage Hub = new("hub.html", new Size(1350, 850), new Size(1140, 750));

    /// <summary>Fixed size, so no maximize (DESIGN §5).</summary>
    public static readonly SurfacePage Setup = new("setup.html", new Size(900, 620), MinimumSize: null);

    public bool Resizable => MinimumSize is not null;
}

/// <summary>
/// The window around a Hub, Setup or flyout page (spec §2). Frameless: the title bar is removed
/// through <c>WM_NCCALCSIZE</c>, but the window keeps its sizing frame, so resizing, Aero Snap
/// and the minimize and maximize animations still work. The page draws its own title bar;
/// <c>app-region: drag</c> areas move the window and its buttons call <c>window.*</c>.
/// </summary>
internal sealed class SurfaceForm : Form
{
    private readonly SurfacePage _page;
    private readonly WebViewRuntime _runtime;
    private readonly Action<BridgeDispatcher> _registerCommands;
    private WebSurface? _surface;
    private bool _reportedMaximized;

    public SurfaceForm(WebViewRuntime runtime, SurfacePage page, Action<BridgeDispatcher> registerCommands)
    {
        _runtime = runtime;
        _page = page;
        _registerCommands = registerCommands;

        Text = "VoiceCtrl";
        Icon = Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = page.Resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
        MaximizeBox = page.Resizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = runtime.Appearance.Background;

        _runtime.Appearance.Changed += OnAppearanceChanged;
    }

    /// <summary>Raised once the page has loaded, for callers that time the first show.</summary>
    public event Action? PageLoaded;

    /// <summary>Sends a <c>[BridgeEvent]</c> to this window's page; call on the UI thread.
    /// Dropped while the page isn't there.</summary>
    public void Post<TEvent>(TEvent payload)
        where TEvent : class => _surface?.Post(payload);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkFrame();

        // Re-runs WM_NCCALCSIZE now, so the frame is the frameless one before anything is sized.
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Win32.SWP_FRAMECHANGED | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

        // DESIGN §5 sizes are what the user sees: the client area, since the side and bottom
        // borders that remain are Windows' invisible resize margins.
        Size = FitToScreen(Scale(_page.Size, WindowDpi()) + FrameSize());

        _surface = new WebSurface(_runtime, Handle, _page.File, dispatcher =>
        {
            RegisterWindowCommands(dispatcher);
            _registerCommands(dispatcher);
        });
        _surface.PageLoaded += () => PageLoaded?.Invoke();
        _surface.SetBounds(ClientRectangle);
        _ = StartSurfaceAsync(_surface);
    }

    private async Task StartSurfaceAsync(WebSurface surface)
    {
        try
        {
            await surface.StartAsync();
            if (ContainsFocus)
            {
                surface.Focus();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Core.Logging.SimpleFileLogger.LogError($"WebView2 start ({_page.File})", ex);
        }
    }

    private void RegisterWindowCommands(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<MinimizeWindow>(_ =>
        {
            WindowState = FormWindowState.Minimized;
            return Task.CompletedTask;
        });
        dispatcher.Handle<ToggleMaximizeWindow>(_ =>
        {
            if (_page.Resizable)
            {
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            }

            return Task.CompletedTask;
        });
        dispatcher.Handle<CloseWindow>(_ =>
        {
            // After the reply is posted, so the page's promise settles before the window goes.
            BeginInvoke(Close);
            return Task.CompletedTask;
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _surface?.SetBounds(ClientRectangle);

        // Maximize and restore arrive here whoever caused them (the page's button, Snap, a
        // double-click on the title strip), so the page's caption glyph follows every one.
        bool maximized = IsHandleCreated && IsMaximized;
        if (maximized != _reportedMaximized)
        {
            _reportedMaximized = maximized;
            _surface?.Post(new WindowStateChanged(maximized));
        }
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        _surface?.NotifyParentMoved();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _surface?.Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _runtime.Appearance.Changed -= OnAppearanceChanged;
        _surface?.Dispose();
        _surface = null;
        base.OnFormClosed(e);
    }

    private void OnAppearanceChanged()
    {
        BackColor = _runtime.Appearance.Background;
        ApplyDarkFrame();
    }

    /// <summary>Windows 11 draws the thin window border itself; match it to the Theme.</summary>
    private void ApplyDarkFrame()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        int dark = _runtime.Appearance.Theme == Theme.Dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Win32.WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
                RemoveTitleBar(ref m);
                return;
            case Win32.WM_NCHITTEST:
                base.WndProc(ref m);
                if (m.Result == Win32.HTCLIENT)
                {
                    m.Result = HitTestClient(m.LParam);
                }

                return;
            case Win32.WM_GETMINMAXINFO when _page.MinimumSize is { } minimum:
                base.WndProc(ref m);
                var info = Marshal.PtrToStructure<Win32.MINMAXINFO>(m.LParam);
                Size scaled = FitToScreen(Scale(minimum, WindowDpi()) + FrameSize());
                info.ptMinTrackSize = new Win32.POINT { X = scaled.Width, Y = scaled.Height };
                Marshal.StructureToPtr(info, m.LParam, false);
                return;
            default:
                base.WndProc(ref m);
                return;
        }
    }

    /// <summary>
    /// Lets Windows compute the client area with the full frame, then gives the caption (and the
    /// top border) back to the client. The side and bottom borders stay, as Windows' own invisible
    /// resize margins. Maximized, Windows places the window a frame's width past the screen edge,
    /// so the top keeps that inset or the page's top row would be cut off.
    /// </summary>
    private void RemoveTitleBar(ref Message m)
    {
        var rect = Marshal.PtrToStructure<Win32.RECT>(m.LParam);
        int originalTop = rect.Top;

        base.WndProc(ref m);

        rect = Marshal.PtrToStructure<Win32.RECT>(m.LParam);
        rect.Top = originalTop + (IsMaximized ? TopFrameThickness() : 0);
        Marshal.StructureToPtr(rect, m.LParam, false);
        m.Result = IntPtr.Zero;
    }

    /// <summary>
    /// A point reaches the form's own client area only where the page isn't (or where the page
    /// hands it back for <c>app-region: drag</c>). Near the top edge it resizes, like the title bar
    /// it replaces; anywhere else it moves the window.
    /// </summary>
    private IntPtr HitTestClient(IntPtr lParam)
    {
        Point point = PointToClient(new Point(unchecked((short)(long)lParam), unchecked((short)((long)lParam >> 16))));
        int band = TopFrameThickness();
        if (_page.Resizable && !IsMaximized && point.Y < band)
        {
            if (point.X < band)
            {
                return Win32.HTTOPLEFT;
            }

            return point.X >= ClientSize.Width - band ? Win32.HTTOPRIGHT : Win32.HTTOP;
        }

        return Win32.HTCAPTION;
    }

    // WinForms' WindowState lags behind while Windows is still maximizing; ask Windows.
    private bool IsMaximized => Win32.IsZoomed(Handle);

    private int TopFrameThickness()
    {
        uint dpi = WindowDpi();
        return Win32.GetSystemMetricsForDpi(Win32.SM_CYSIZEFRAME, dpi) + Win32.GetSystemMetricsForDpi(Win32.SM_CXPADDEDBORDER, dpi);
    }

    /// <summary>The invisible resize margins around the client area, read from Windows rather
    /// than WinForms' cached sizes, which assume the standard caption until the first resize.</summary>
    private Size FrameSize()
    {
        Win32.GetWindowRect(Handle, out Win32.RECT window);
        Win32.GetClientRect(Handle, out Win32.RECT client);
        return new Size(window.Right - window.Left - client.Right, window.Bottom - window.Top - client.Bottom);
    }

    /// <summary>Keeps a size inside the usable area of the screen the window is on, so the larger
    /// Hub still opens whole, and can still be resized, on a small laptop screen.</summary>
    private Size FitToScreen(Size pixels)
    {
        Rectangle area = Screen.FromHandle(Handle).WorkingArea;
        return new Size(Math.Min(pixels.Width, area.Width), Math.Min(pixels.Height, area.Height));
    }

    private uint WindowDpi() => IsHandleCreated ? Win32.GetDpiForWindow(Handle) : (uint)DeviceDpi;

    private static Size Scale(Size dips, uint dpi) =>
        new((int)Math.Round(dips.Width * dpi / 96.0), (int)Math.Round(dips.Height * dpi / 96.0));
}
