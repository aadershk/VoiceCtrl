using System.Diagnostics;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Input;

/// <summary>
/// The dedicated input thread (spec §2): both low-level hooks and their own message loop, so a
/// busy UI thread (a WebView2 starting up, say) can never push a hook past Windows' timeout. The
/// hooks only classify; every gesture is posted to the UI thread, where its event is raised. The
/// two decisions that must be made synchronously (swallow Esc, swallow mouse input over the
/// Overlay) read state the UI thread keeps current.
/// </summary>
internal sealed class InputThread : IDisposable
{
    // Thread messages from the UI thread; the hook may only be touched on this thread.
    private const uint RebindMessage = Win32.WM_APP + 1;
    private const uint CaptureMessage = Win32.WM_APP + 2;

    private readonly SynchronizationContext _ui;
    private readonly int _doubleTapWindowMs;
    private readonly Func<bool> _shouldSwallowEscape;
    private readonly ManualResetEventSlim _started = new();
    private volatile HotkeySettings _hotkeys;
    private Thread? _thread;
    private uint _threadId;
    private Exception? _startFailure;

    // Mouse moves are coalesced: at most one is queued to the UI thread, carrying the newest point
    // when it runs, so a 1000 Hz mouse can't flood the UI thread.
    private long _latestMove;
    private int _movePosted;
    private long _lastGestureTimestamp;

    /// <param name="shouldSwallowEscape">Read on the input thread; must be cheap and thread-safe
    /// (the Dictation engine's volatile flag).</param>
    public InputThread(SynchronizationContext ui, int doubleTapWindowMs, HotkeySettings hotkeys, Func<bool> shouldSwallowEscape)
    {
        _ui = ui;
        _doubleTapWindowMs = doubleTapWindowMs;
        _hotkeys = hotkeys;
        _shouldSwallowEscape = shouldSwallowEscape;
    }

    // All raised on the UI thread.
    public event Action? HotkeyDoubleTapped;
    public event Action? TriggerTapped;
    public event Action? TriggerHoldStarted;
    public event Action? TriggerHoldEnded;
    public event Action? RewriteHoldStarted;
    public event Action? RewriteHoldEnded;
    public event Action? RewriteHoldCancelled;
    public event Action? EscapePressed;

    /// <summary>Pointer input over the Overlay's hit rectangle (physical screen pixels), raised on
    /// the UI thread in order. Moves are coalesced to the newest point.</summary>
    public event Action<OverlayPointerKind, int, int>? OverlayPointer;

    /// <summary>When the latest gesture (a Hotkey or Trigger Key gesture, or a press on the
    /// Overlay) left the hook, in <see cref="Stopwatch"/> ticks: the start of the Overlay's show
    /// latency (spec §7). Zero before the first.</summary>
    public long LastGestureTimestamp => Interlocked.Read(ref _lastGestureTimestamp);

    /// <summary>While capturing: a key went down (true) or up (false). Raised on the UI thread,
    /// in the order the keys were pressed.</summary>
    public event Action<int, bool>? CaptureKey;

    /// <summary>The mouse hook; the Overlay sets its hit rectangle.</summary>
    public MouseHook Mouse { get; } = new();

    /// <summary>Starts the thread and returns once both hooks are installed. Throws if either fails.</summary>
    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "VoiceCtrl input hooks" };
        _thread.Start();
        _started.Wait();
        if (_startFailure is not null)
        {
            throw new InvalidOperationException("The input hooks could not start.", _startFailure);
        }
    }

    /// <summary>New Hotkey and Trigger Key bindings (Settings apply live). Any thread.</summary>
    public void Rebind(HotkeySettings hotkeys)
    {
        _hotkeys = hotkeys;
        Win32.PostThreadMessage(_threadId, RebindMessage, UIntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Starts or stops handing every key to <see cref="CaptureKey"/> instead of the gestures, and
    /// swallowing it. Only for a "Press a key" row in the focused Hub: the owner of the capture
    /// must stop it when the Hub loses focus, or typing elsewhere would vanish.
    /// </summary>
    public void SetCapturing(bool capturing) =>
        Win32.PostThreadMessage(_threadId, CaptureMessage, capturing ? 1u : 0u, IntPtr.Zero);

    private void Run()
    {
        _threadId = Win32.GetCurrentThreadId();
        using var keyboard = new KeyboardHook(_doubleTapWindowMs, _hotkeys, _shouldSwallowEscape);
        keyboard.DoubleTapDetected += () => Post(HotkeyDoubleTapped);
        keyboard.TriggerTapped += () => Post(TriggerTapped);
        keyboard.TriggerHoldStarted += () => Post(TriggerHoldStarted);
        keyboard.TriggerHoldEnded += () => Post(TriggerHoldEnded);
        keyboard.RewriteHoldStarted += () => Post(RewriteHoldStarted);
        keyboard.RewriteHoldEnded += () => Post(RewriteHoldEnded);
        keyboard.RewriteHoldCancelled += () => Post(RewriteHoldCancelled);
        keyboard.EscapeSwallowed += () => Post(EscapePressed);
        keyboard.CaptureKey += (vk, down) => _ui.Post(_ => Raise(() => CaptureKey?.Invoke(vk, down)), null);
        Mouse.Pointer += OnPointer;

        try
        {
            keyboard.Install();
            Mouse.Install();
        }
        catch (InvalidOperationException ex)
        {
            _startFailure = ex;
            _started.Set();
            return;
        }

        _started.Set();

        // GetMessage returns 0 on WM_QUIT (Dispose) and -1 on error.
        while (Win32.GetMessage(out Win32.MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            // Thread messages have no window, so DispatchMessage would drop them.
            if (message.hwnd == IntPtr.Zero && message.message == RebindMessage)
            {
                keyboard.Bind(_hotkeys);
                continue;
            }

            if (message.hwnd == IntPtr.Zero && message.message == CaptureMessage)
            {
                keyboard.SetCapturing(message.wParam != UIntPtr.Zero);
                continue;
            }

            Win32.TranslateMessage(message);
            Win32.DispatchMessage(message);
        }

        Mouse.Dispose();
    }

    // Post returns at once, so the hook callback stays inside Windows' time budget.
    private void Post(Action? handler)
    {
        Interlocked.Exchange(ref _lastGestureTimestamp, Stopwatch.GetTimestamp());
        _ui.Post(_ => Raise(handler), null);
    }

    // On the hook thread.
    private void OnPointer(OverlayPointerKind kind, int x, int y)
    {
        if (kind != OverlayPointerKind.Move)
        {
            if (kind == OverlayPointerKind.Down)
            {
                Interlocked.Exchange(ref _lastGestureTimestamp, Stopwatch.GetTimestamp());
            }

            _ui.Post(_ => Raise(() => OverlayPointer?.Invoke(kind, x, y)), null);
            return;
        }

        Interlocked.Exchange(ref _latestMove, ((long)x << 32) | (uint)y);
        if (Interlocked.Exchange(ref _movePosted, 1) == 0)
        {
            _ui.Post(_ =>
            {
                Volatile.Write(ref _movePosted, 0);
                long packed = Interlocked.Read(ref _latestMove);
                Raise(() => OverlayPointer?.Invoke(OverlayPointerKind.Move, (int)(packed >> 32), (int)packed));
            }, null);
        }
    }

    private static void Raise(Action? handler)
    {
        try
        {
            handler?.Invoke();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Input gesture", ex);
        }
    }

    public void Dispose()
    {
        if (_thread is { IsAlive: true })
        {
            Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _started.Dispose();
    }
}
