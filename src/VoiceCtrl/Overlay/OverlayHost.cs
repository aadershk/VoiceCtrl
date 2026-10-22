using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Input;
using VoiceCtrl.Interop;
using VoiceCtrl.Web;

namespace VoiceCtrl.Overlay;

/// <summary>
/// The Overlay (spec §2, §7): a raw Win32 window built to the spike's three rules (research 15),
/// holding <c>overlay.html</c> on a transparent WebView2, created at start and kept warm for the
/// whole run. The window is never activated and never takes input: it is always click-through,
/// and the mouse hook forwards pointer input over the pill to the page. Renders the Dictation
/// engine's view, streams the live level, places the window at its dock on the active monitor on
/// each show, moves it while the pill is dragged and slides it to the nearest dock on release, and
/// logs the show latency (gesture to the page's second animation frame; the gate is 50 ms).
/// </summary>
internal sealed partial class OverlayHost : IDisposable
{
    public const string Page = "overlay.html";

    /// <summary>A gesture older than this didn't cause the show (a Dictation outcome or a notice
    /// did), so the latency is timed from the view change instead.</summary>
    private static readonly long GestureWindowTicks = Stopwatch.Frequency / 2;

    private readonly SynchronizationContext _ui;
    private readonly DictationController _controller;
    private readonly InputThread _input;
    private readonly SettingsStore _settings;
    private readonly Func<bool> _paused;
    private readonly Func<bool> _reducedMotion;
    private readonly TaskbarThemeWatcher _taskbar = new();
    private readonly OverlayWindow _window;
    private readonly OverlayWindowMotion _motion;
    private readonly WebSurface _surface;
    private double _dpiScale = 1;
    private OverlayDock _dock;
    private PageRect? _pageRect;
    private OverlayState _state;
    private int _seq;
    private (int Seq, long Start)? _pendingShow;
    private OverlayDrag? _press;
    private volatile bool _dragging;
    private bool _startFailed;
    private bool _disposed;

    /// <param name="registerShared">Registers the commands every surface has (<c>app.*</c>).</param>
    /// <param name="paused">Pause (DESIGN §12): the Ready tile doesn't start a Dictation while it's on.</param>
    /// <param name="reducedMotion">Windows' "Animation effects" is off: the dock snap jumps.</param>
    public OverlayHost(
        WebViewRuntime runtime,
        SynchronizationContext ui,
        DictationController controller,
        InputThread input,
        SettingsStore settings,
        Action<BridgeDispatcher> registerShared,
        Func<bool> paused,
        Func<bool> reducedMotion)
    {
        _ui = ui;
        _controller = controller;
        _input = input;
        _settings = settings;
        _paused = paused;
        _reducedMotion = reducedMotion;
        _dock = settings.Current.Overlay.Dock;

        // The handle rests on screen only once there is an Overlay to draw it.
        controller.AlwaysShow = settings.Current.Overlay.AlwaysShow;
        _state = OverlayState.From(controller.View, _seq);

        _window = new OverlayWindow(this);
        _motion = new OverlayWindowMotion(_window.Handle);
        _motion.Moved += OnWindowMoved;
        Place();
        Win32.ShowWindow(_window.Handle, Win32.SW_SHOWNOACTIVATE);

        _surface = new WebSurface(runtime, _window.Handle, Page, dispatcher =>
        {
            registerShared(dispatcher);
            RegisterCommands(dispatcher);
        }, transparent: true);
        _surface.SetBounds(new Rectangle(0, 0, _motion.Rect.Width, _motion.Rect.Height));
        _motion.Landed += _surface.NotifyParentMoved;

        controller.ViewChanged += OnViewChanged;
        controller.LevelChanged += OnLevel;
        input.OverlayPointer += OnPointer;
        settings.OverlayChanged += OnOverlaySettingsChanged;
        _taskbar.TaskbarThemeChanged += OnTaskbarThemeChanged;
    }

    /// <summary>"Add an API key" was clicked: open Settings at the key row.</summary>
    public event Action? KeySettingsRequested;

    /// <summary>A press on the pill has become a drag. Read by the keyboard hook's thread, so Esc
    /// is swallowed while it lasts.</summary>
    public bool IsDragging => _dragging;

    private OverlayLayout Layout => new(_dock, _taskbar.IsTaskbarLightTheme ? Theme.Light : Theme.Dark);

    public async Task StartAsync()
    {
        try
        {
            await _surface.StartAsync();
            _startFailed = false;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The next show tries again (spec §3: the show path checks the Overlay is alive).
            _startFailed = true;
            SimpleFileLogger.LogError("Overlay start", ex);
        }
    }

    /// <summary>Esc during a drag: the window slides back to where the press began and the dock is
    /// unchanged. False when there is no drag, so Esc means Cancel as usual.</summary>
    public bool CancelDrag()
    {
        if (_press is not { IsDragging: true } press)
        {
            return false;
        }

        EndDrag();
        _motion.Slide(_motion.Rect, press.StartWindow, _reducedMotion());
        return true;
    }

    private void OnViewChanged(OverlayView view)
    {
        bool wasHidden = _state.Phase == OverlayPhase.Hidden;
        _state = OverlayState.From(view, ++_seq);

        if (view.Phase == OverlayPhase.Hidden)
        {
            // Never wait for the page to say the pill is gone: a stale rectangle would keep
            // swallowing clicks meant for the app underneath.
            _pageRect = null;
            _input.Mouse.HitRect = null;
            _pendingShow = null;
        }
        else if (wasHidden)
        {
            long now = Stopwatch.GetTimestamp();
            long gesture = LatestGesture();
            _pendingShow = (_state.Seq, gesture != 0 && now - gesture < GestureWindowTicks ? gesture : now);

            // Only a show from hidden follows the active monitor: the resting handle stays where
            // it is, so its Ready tile doesn't jump away from the pointer that is about to click it.
            if (_press is null && !_motion.IsSliding)
            {
                Place();
            }

            EnsureAlive();
        }

        _surface.Post(new OverlayViewChanged(_state));
    }

    private long LatestGesture()
    {
        long gesture = _input.LastGestureTimestamp;
#if DEBUG
        gesture = Math.Max(gesture, _debugGesture);
#endif
        return gesture;
    }

    // Raised on the recorder's capture thread; WebView2 is used only on the UI thread.
    private void OnLevel(float level) => _ui.Post(_ => _surface.Post(new OverlayLevel(level)), null);

    private void OnPointer(OverlayPointerKind kind, int screenX, int screenY)
    {
        if (_state.Phase == OverlayPhase.Hidden && kind != OverlayPointerKind.Leave && _press is null)
        {
            return;
        }

        switch (kind)
        {
            case OverlayPointerKind.Down:
                _motion.Stop();
                _press = new OverlayDrag(screenX, screenY, _motion.Rect, _dpiScale);
                break;

            case OverlayPointerKind.Move when _press is { } press:
                bool wasDragging = press.IsDragging;
                if (press.Move(screenX, screenY) is { } moved)
                {
                    if (!wasDragging)
                    {
                        _dragging = true;
                        _surface.Post(new OverlayDragChanged(true));
                    }

                    _motion.MoveTo(moved);
                    return;
                }

                break;

            case OverlayPointerKind.Up when _press is { IsDragging: true }:
                Drop(screenX, screenY);
                return;

            case OverlayPointerKind.Up:
                _press = null;
                break;
        }

        // While dragging the pointer rides along with the pill; the page hears only the drag.
        if (_press is { IsDragging: true })
        {
            return;
        }

        (double x, double y) = OverlayGeometry.ToPage(screenX, screenY, _motion.Rect, _dpiScale);
        _surface.Post(new OverlayPointer(kind, x, y));
    }

    /// <summary>The button came up after a drag: snap to the dock nearest the pill, on the monitor
    /// under the pointer, and remember it.</summary>
    private void Drop(int screenX, int screenY)
    {
        EndDrag();
        IntPtr monitorHandle = MonitorPositioner.FindMonitorAt(screenX, screenY);
        if (MonitorPositioner.Describe(monitorHandle) is not { } monitor)
        {
            return;
        }

        // The pill went away mid-drag (a Pasted that timed out): the resting tile's place stands in.
        PageRect pill = _pageRect ?? new PageRect(
            OverlayGeometry.PillLeftDip(_dock, 32), OverlayGeometry.PillCenterYDip - 16, 32, 32);

        PixelRect window = _motion.Rect;
        int pillX = window.Left + (int)Math.Round((pill.X + (pill.Width / 2)) * _dpiScale);
        int pillY = window.Top + (int)Math.Round((pill.Y + (pill.Height / 2)) * _dpiScale);
        OverlayDock dock = OverlayGeometry.NearestDock(pillX, pillY, monitor.WorkArea, monitor.DpiScale);

        SnapTo(dock, monitor.WorkArea, monitor.DpiScale, pill.Width);
        if (dock != _settings.Current.Overlay.Dock)
        {
            // OnOverlaySettingsChanged then finds the dock already applied.
            _settings.Update(s => s with { Overlay = s.Overlay with { Dock = dock } });
        }
    }

    private void EndDrag()
    {
        _press = null;
        _dragging = false;
        _surface.Post(new OverlayDragChanged(false));
    }

    /// <summary>
    /// Moves to <paramref name="dock"/> on a monitor: the page re-anchors the pill for the new dock
    /// while the window jumps by the same amount, so nothing moves yet, then the window slides to
    /// the dock on the spring. A monitor at another DPI needs a new window size, so that jumps.
    /// </summary>
    private void SnapTo(OverlayDock dock, PixelRect workArea, double dpiScale, double pillWidthDip)
    {
        OverlayDock from = _dock;
        _dock = dock;
        _surface.Post(new OverlayLayoutChanged(Layout));

        PixelRect target = OverlayGeometry.Window(workArea, dpiScale, dock);
        if (dpiScale != _dpiScale)
        {
            _dpiScale = dpiScale;
            _motion.Place(target);
            return;
        }

        PixelRect start = OverlayGeometry.Rebase(_motion.Rect, _dpiScale, from, dock, pillWidthDip);
        _motion.Slide(start, target, _reducedMotion());
    }

    /// <summary>The dock changed in Settings (the next slice's row), or Always show was turned on or off.</summary>
    private void OnOverlaySettingsChanged(OverlaySection section) => _ui.Post(_ =>
    {
        _controller.AlwaysShow = section.AlwaysShow;
        if (section.Dock == _dock || _press is not null)
        {
            return;
        }

        if (_state.Phase == OverlayPhase.Hidden)
        {
            _dock = section.Dock;
            _surface.Post(new OverlayLayoutChanged(Layout));
            Place();
        }
        else if (MonitorPositioner.Describe(MonitorPositioner.FindMonitorOf(_window.Handle)) is { } monitor)
        {
            SnapTo(section.Dock, monitor.WorkArea, monitor.DpiScale, _pageRect?.Width ?? 32);
        }
    }, null);

    private void OnTaskbarThemeChanged() => _ui.Post(_ => _surface.Post(new OverlayLayoutChanged(Layout)), null);

    private void RegisterCommands(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<GetOverlayState, OverlayState>(_ => Task.FromResult(_state));
        dispatcher.Handle<GetOverlayLayout, OverlayLayout>(_ => Task.FromResult(Layout));
        dispatcher.Handle<SetOverlayHitRect>(args =>
        {
            // A report for a pill that has since gone is ignored; see OnViewChanged.
            _pageRect = _state.Phase == OverlayPhase.Hidden ? null : new PageRect(args.X, args.Y, args.Width, args.Height);
            UpdateHitRect();
            return Task.CompletedTask;
        });
        dispatcher.Handle<OverlayPainted>(args =>
        {
            if (_pendingShow is { } show && args.Seq >= show.Seq)
            {
                _pendingShow = null;
                double milliseconds = (Stopwatch.GetTimestamp() - show.Start) * 1000.0 / Stopwatch.Frequency;
                SimpleFileLogger.LogInfo($"Overlay: shown in {milliseconds:0.0} ms");
            }

            return Task.CompletedTask;
        });
        dispatcher.Handle<StartFromOverlay>(_ => StartFromTile());
        dispatcher.Handle<CancelFromOverlay>(_ =>
        {
            if (_state.CanCancel)
            {
                _controller.Cancel();
            }

            return Task.CompletedTask;
        });
        dispatcher.Handle<FinishFromOverlay>(command =>
        {
            if (_state.CanFinish)
            {
                _ = _controller.Finish();
            }

            return Task.CompletedTask;
        });
        dispatcher.Handle<PressFromOverlay>(command =>
        {
            _ = _controller.PillPressed();
            return Task.CompletedTask;
        });
        dispatcher.Handle<RetryFromOverlay>(args =>
        {
            // Not awaited: the retry runs as long as the transcription does, and the page only
            // needs to know it was received. The engine ignores a failure it isn't showing.
            _ = _controller.Retry(ParseFailedId(args.FailedId), offline: false);
            return Task.CompletedTask;
        });
        dispatcher.Handle<UndoFromOverlay>(args =>
        {
            _ = _controller.Undo(ParseFailedId(args.FailedId));
            return Task.CompletedTask;
        });
        dispatcher.Handle<DismissFromOverlay>(args =>
        {
            _controller.DismissFailed(ParseFailedId(args.FailedId));
            return Task.CompletedTask;
        });
        dispatcher.Handle<OpenKeySettings>(_ =>
        {
            KeySettingsRequested?.Invoke();
            return Task.CompletedTask;
        });
    }

    private Task StartFromTile()
    {
        if (!_paused())
        {
            _ = _controller.StartFromOverlay();
        }

        return Task.CompletedTask;
    }

    private static Guid ParseFailedId(string id) =>
        Guid.TryParse(id, out Guid parsed) ? parsed : throw new BridgeCommandException("bad_args", "Not a Failed Dictation id.");

    /// <summary>The mouse hook's rectangle, from the page's last report and where the window is
    /// now: it moves with the window during a drag and a snap.</summary>
    private void UpdateHitRect()
    {
        PixelRect? hit = _pageRect is { } pill ? OverlayGeometry.HitRect(pill, _motion.Rect, _dpiScale) : null;
        _input.Mouse.HitRect = hit is { } rect ? new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    }

    private void OnWindowMoved(bool resized)
    {
        UpdateHitRect();

        // The first move comes from the constructor, before the surface exists.
        if (resized && _surface is not null)
        {
            _surface.SetBounds(new Rectangle(0, 0, _motion.Rect.Width, _motion.Rect.Height));
            _surface.NotifyParentMoved();
        }
    }

    /// <summary>
    /// Moves the window to its dock on the monitor the user is working on, at that monitor's DPI,
    /// and back on top of the taskbar (which is topmost too). Nothing moves when the monitor can't
    /// be read.
    /// </summary>
    private void Place()
    {
        if (MonitorPositioner.Describe(MonitorPositioner.FindActiveMonitor()) is not { } monitor)
        {
            return;
        }

        _dpiScale = monitor.DpiScale;
        _motion.Place(OverlayGeometry.Window(monitor.WorkArea, monitor.DpiScale, _dock));
        _surface?.NotifyParentMoved();
    }

    private void EnsureAlive()
    {
        if (_startFailed)
        {
            SimpleFileLogger.LogInfo("Overlay: page not running; starting it again");
            _startFailed = false;
            _ = StartAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.ViewChanged -= OnViewChanged;
        _controller.LevelChanged -= OnLevel;
        _input.OverlayPointer -= OnPointer;
        _settings.OverlayChanged -= OnOverlaySettingsChanged;
        _taskbar.TaskbarThemeChanged -= OnTaskbarThemeChanged;
        _taskbar.Dispose();
        _input.Mouse.HitRect = null;
        _motion.Dispose();
        _surface.Dispose();
        _window.DestroyHandle();
    }
}
