using System.Runtime.InteropServices;
using System.Windows.Threading;
using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Installs a WH_KEYBOARD_LL hook tracking the configured Hotkey's double-tap and, when enabled,
/// the Trigger Key's Tap/Hold gesture (single key or chord). Must be installed from a thread that
/// runs a Win32 message loop (the WPF Dispatcher thread qualifies) — the Trigger Key's hold
/// detection also needs that thread's Dispatcher for its threshold timer, since nothing else can
/// tell "a quick tap" from "the start of a hold" without one (see <see cref="TriggerKeyTracker"/>).
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    /// <summary>How long the Trigger Key must stay down before it counts as Hold rather than Tap.
    /// Not user-configurable — ticket 03/its Addendum fixed the gesture model but left this
    /// internal timing to implementation judgment. Short enough that Hold-to-Talk still feels
    /// immediate, long enough that a deliberate quick tap is never misread as the start of a hold.
    /// </summary>
    private const long TriggerHoldThresholdMs = 200;

    // Kept as a field, not a local/lambda: if this delegate were GC-eligible while native
    // code still held the function pointer, the hook would break unpredictably at collection.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly CtrlKeyTracker _hotkeyTracker;
    private readonly TriggerKeyTracker? _triggerTracker;
    private readonly DispatcherTimer? _triggerHoldTimer;
    private IntPtr _hookHandle = IntPtr.Zero;

    public event Action? DoubleTapDetected
    {
        add => _hotkeyTracker.DoubleTapDetected += value;
        remove => _hotkeyTracker.DoubleTapDetected -= value;
    }

    /// <summary>Raised on a quick Trigger Key press+release (or a completed chord tap). No-op to
    /// subscribe to when the Trigger Key is off — it simply never fires.</summary>
    public event Action? TriggerTapped
    {
        add { if (_triggerTracker is not null) _triggerTracker.Tapped += value; }
        remove { if (_triggerTracker is not null) _triggerTracker.Tapped -= value; }
    }

    /// <summary>Raised once the Trigger Key (or chord) has been held past the hold threshold.</summary>
    public event Action? TriggerHoldStarted
    {
        add { if (_triggerTracker is not null) _triggerTracker.HoldStarted += value; }
        remove { if (_triggerTracker is not null) _triggerTracker.HoldStarted -= value; }
    }

    /// <summary>Raised when a held Trigger Key (or chord) is released.</summary>
    public event Action? TriggerHoldEnded
    {
        add { if (_triggerTracker is not null) _triggerTracker.HoldEnded += value; }
        remove { if (_triggerTracker is not null) _triggerTracker.HoldEnded -= value; }
    }

    public LowLevelKeyboardHook(int doubleTapWindowMs, HotkeySettings hotkeySettings)
    {
        _proc = HookCallback;
        _hotkeyTracker = new CtrlKeyTracker(doubleTapWindowMs, HotkeyKeyCatalog.ResolveVkCodes(hotkeySettings.HotkeyKey));

        if (hotkeySettings.IsTriggerReady)
        {
            (int primaryVk, int? secondVk) = ResolveTriggerVks(hotkeySettings);
            _triggerTracker = new TriggerKeyTracker(primaryVk, secondVk, TriggerHoldThresholdMs);
            _triggerHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TriggerHoldThresholdMs) };
            _triggerHoldTimer.Tick += (_, _) =>
            {
                _triggerHoldTimer.Stop();
                _triggerTracker.CheckHoldThreshold(Environment.TickCount64);
            };
        }
    }

    private static (int PrimaryVk, int? SecondVk) ResolveTriggerVks(HotkeySettings settings)
    {
        if (settings.TriggerMode == HotkeySettings.TriggerModeChord)
        {
            // Both guaranteed non-null here: HotkeySettings.IsTriggerReady already checked this.
            int prefixVk = HotkeyKeyCatalog.ResolveVkCodes(settings.ChordPrefix!)[0];
            return (prefixVk, settings.ChordSecondKeyVk!.Value);
        }

        int keyVk = HotkeyKeyCatalog.ResolveVkCodes(settings.TriggerKey!)[0];
        return (keyVk, null);
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, moduleHandle, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to install low-level keyboard hook (Win32 error {error}).");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Budget: this runs on the WPF UI thread inside Windows' hook-dispatch path. A callback
        // that runs too long risks Windows silently unhooking it, and since this hook shares the
        // UI thread, a slow callback also stalls the whole app. Do only cheap work here and defer
        // anything UI-visible via Dispatcher.BeginInvoke at the call site (see App.OnDoubleTapDetected).
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            // Filtering LLKHF_INJECTED runs before any VK-specific logic. It's what stops our own
            // synthetic Ctrl+V paste (sent with VK_LCONTROL) from ever being misread as a real
            // Left-Ctrl press, and does the same for whatever key the Hotkey/Trigger Key happen to
            // be configured to.
            if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                int message = wParam.ToInt32();
                int normalizedVk = PhysicalKeyResolver.Normalize(data.vkCode, data.flags);
                long now = Environment.TickCount64;

                if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    _hotkeyTracker.OnKeyDown(normalizedVk, now);
                    HandleTriggerKeyDown(normalizedVk, now);
                }
                else if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    _hotkeyTracker.OnKeyUp(normalizedVk);
                    HandleTriggerKeyUp(normalizedVk, now);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void HandleTriggerKeyDown(int vk, long now)
    {
        if (_triggerTracker is null)
        {
            return;
        }

        bool wasEngaged = _triggerTracker.IsEngaged;
        _triggerTracker.OnKeyDown(vk, now);

        if (!wasEngaged && _triggerTracker.IsEngaged)
        {
            _triggerHoldTimer!.Stop();
            _triggerHoldTimer.Start();
        }
    }

    private void HandleTriggerKeyUp(int vk, long now)
    {
        if (_triggerTracker is null)
        {
            return;
        }

        _triggerTracker.OnKeyUp(vk, now);
        _triggerHoldTimer!.Stop();
    }

    public void Dispose()
    {
        _triggerHoldTimer?.Stop();

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
