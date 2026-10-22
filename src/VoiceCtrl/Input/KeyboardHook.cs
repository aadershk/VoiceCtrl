using System.Runtime.InteropServices;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Input;

/// <summary>
/// The WH_KEYBOARD_LL hook (spec §2): tracks the Hotkey's double-tap, the Trigger Key's Tap and
/// Hold (single key, chord or combination), the Rewrite key's Hold, and Esc while a Dictation may be cancelled. While a Settings row
/// says "Press a key" it instead hands every key to the capture and swallows it, so the key never
/// reaches the Hub or any other app. Lives on <see cref="InputThread"/>: it must be installed,
/// rebound and disposed there, and its events fire there, so they only classify; the thread posts
/// them on to the UI thread.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    /// <summary>How long the Trigger Key must stay down before the hold starts: none. Recording
    /// begins on the key press so the first words are kept; the controller drops a hold shorter than
    /// a tap silently (VED-74).</summary>
    private const uint TriggerHoldThresholdMs = 0;

    /// <summary>How long a combination that is also a Windows shortcut (Ctrl + Shift: the input
    /// language switch) stays untouched. Recording starts at the key press, but Windows is only kept
    /// from acting on the keys once they have been held this long, matching the controller's tap
    /// threshold: a quick tap still switches the language, and the user is told about that clash
    /// where they choose the keys (VED-74).</summary>
    private const uint ShortcutBreakDelayMs = 500;

    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;

    /// <summary>An unassigned key code. Pressing it between a modifier going down and coming up
    /// tells Windows the modifier was part of a shortcut, so a Win key in a combination never
    /// opens the Start menu, nor Alt the menu bar, nor Ctrl+Shift the language switch.</summary>
    private const int ShortcutBreakerVk = 0xFF;

    // Fields, not locals: if a delegate were collected while native code still held its pointer,
    // the hook or timer would fail unpredictably.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly int _doubleTapWindowMs;
    private readonly Func<bool> _shouldSwallowEscape;

    /// <summary>Keys whose key-down the capture swallowed. Their key-ups are swallowed too, even
    /// after the capture ends, so no app ever sees half a keystroke.</summary>
    private readonly HashSet<int> _swallowedDown = [];
    private CtrlKeyTracker _hotkeyTracker = null!;
    private HoldBinding? _trigger;
    private HoldBinding? _rewrite;
    private IntPtr _hookHandle;
    private readonly Dictionary<UIntPtr, HoldBinding> _timerOwners = [];
    private bool _escapeDownSwallowed;
    private bool _capturing;

    public KeyboardHook(int doubleTapWindowMs, HotkeySettings hotkeys, Func<bool> shouldSwallowEscape)
    {
        _proc = HookCallback;
        _doubleTapWindowMs = doubleTapWindowMs;
        _shouldSwallowEscape = shouldSwallowEscape;
        Bind(hotkeys);
    }

    public event Action? DoubleTapDetected;

    /// <summary>A quick Trigger Key press and release. Never fires when the Trigger Key is off.</summary>
    public event Action? TriggerTapped;

    public event Action? TriggerHoldStarted;

    public event Action? TriggerHoldEnded;

    /// <summary>The Rewrite key was held past the threshold, and let go. A quick tap does nothing.</summary>
    public event Action? RewriteHoldStarted;

    public event Action? RewriteHoldEnded;

    /// <summary>Another key went down during a Rewrite hold: a shortcut, not a rewrite.</summary>
    public event Action? RewriteHoldCancelled;

    /// <summary>Esc went down while <c>shouldSwallowEscape</c> said yes; the key never reaches the app.</summary>
    public event Action? EscapeSwallowed;

    /// <summary>While capturing: a key went down (true) or up (false), normalized to its side.</summary>
    public event Action<int, bool>? CaptureKey;

    /// <summary>
    /// Rebuilds the trackers from new bindings (Settings apply live). The old trackers are simply
    /// dropped: their state belongs to keys that no longer mean anything.
    /// </summary>
    public void Bind(HotkeySettings hotkeys)
    {
        StopHoldTimers();

        var hotkeyTracker = new CtrlKeyTracker(_doubleTapWindowMs, HotkeyKeyCatalog.ResolveVkCodes(hotkeys.HotkeyKey), IsKeyActuallyDown);
        hotkeyTracker.DoubleTapDetected += () => DoubleTapDetected?.Invoke();
        _hotkeyTracker = hotkeyTracker;

        _trigger = null;
        if (hotkeys.IsTriggerReady)
        {
            _trigger = CreateBinding(
                hotkeys.TriggerVks, TriggerHoldThresholdMs, otherKeySpoilsHold: false,
                () => TriggerTapped?.Invoke(), () => TriggerHoldStarted?.Invoke(), () => TriggerHoldEnded?.Invoke(), cancelled: null);
        }

        // Ctrl + Shift (the default) starts many shortcuts, so another key before the hold begins
        // makes it one of those and not a rewrite.
        _rewrite = hotkeys.RewriteKeys is { Count: > 0 } rewriteVks
            ? CreateBinding(rewriteVks, TriggerHoldThresholdMs, otherKeySpoilsHold: true, tapped: null, () => RewriteHoldStarted?.Invoke(), () => RewriteHoldEnded?.Invoke(), () => RewriteHoldCancelled?.Invoke())
            : null;
    }

    private HoldBinding CreateBinding(IReadOnlyList<int> vks, uint holdThresholdMs, bool otherKeySpoilsHold, Action? tapped, Action holdStarted, Action holdEnded, Action? cancelled)
    {
        var tracker = new TriggerKeyTracker(vks, holdThresholdMs, IsKeyActuallyDown, otherKeySpoilsHold);
        if (tapped is not null)
        {
            tracker.Tapped += tapped;
        }

        var binding = new HoldBinding(tracker, vks.Count > 1 ? vks.FirstOrDefault(vk => !KeyNames.IsModifier(vk)) : 0, OnHoldTimerFor, deferShortcutBreak: otherKeySpoilsHold, holdThresholdMs, usesWinKey: vks.Any(vk => vk is VkLeftWin or VkRightWin));

        // The recording starts at once; for a binding that can also be an ordinary shortcut,
        // Windows is kept from acting on it only after ShortcutBreakDelayMs (see HandleKeyDown).
        tracker.HoldStarted += () =>
        {
            binding.HoldActive = true;
            holdStarted();
        };
        tracker.HoldEnded += () =>
        {
            binding.HoldActive = false;
            holdEnded();
        };
        if (cancelled is not null)
        {
            tracker.HoldCancelled += () =>
            {
                binding.HoldActive = false;
                cancelled();
            };
        }

        return binding;
    }

    /// <summary>Starts or stops handing keys to the capture instead of the trackers.</summary>
    public void SetCapturing(bool capturing) => _capturing = capturing;

    public void Install()
    {
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to install the keyboard hook (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>The real key state, which the trackers cross-check before a stale flag could complete
    /// a false chord or swallow a double-tap.</summary>
    private static bool IsKeyActuallyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Runs inside Windows' hook dispatch with a strict time budget: classify only.
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            // Skipping injected keys stops our own synthetic Ctrl+V (sent with VK_LCONTROL) from
            // ever reading as a real Ctrl press.
            if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                int message = wParam.ToInt32();
                bool isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                bool isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
                int normalizedVk = PhysicalKeyResolver.Normalize(data.vkCode, data.flags);

                if (HandleCapture(normalizedVk, isDown, isUp))
                {
                    return 1;
                }

                if (data.vkCode == Win32.VK_ESCAPE && HandleEscape(isDown, isUp))
                {
                    return 1;
                }

                long now = Environment.TickCount64;
                bool swallow = false;
                if (isDown)
                {
                    _hotkeyTracker.OnKeyDown(normalizedVk, now);

                    // Both always run: each binding must see every key.
                    bool swallowTrigger = HandleKeyDown(_trigger, normalizedVk, now);
                    bool swallowRewrite = HandleKeyDown(_rewrite, normalizedVk, now);
                    swallow = swallowTrigger || swallowRewrite;
                }
                else if (isUp)
                {
                    _hotkeyTracker.OnKeyUp(normalizedVk);
                    bool swallowTrigger = HandleKeyUp(_trigger, normalizedVk, now);
                    bool swallowRewrite = HandleKeyUp(_rewrite, normalizedVk, now);
                    swallow = swallowTrigger || swallowRewrite;
                }

                if (swallow)
                {
                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>True when this key event belongs to a capture and must be swallowed.</summary>
    private bool HandleCapture(int vk, bool isDown, bool isUp)
    {
        if (isUp && _swallowedDown.Remove(vk))
        {
            if (_capturing)
            {
                CaptureKey?.Invoke(vk, false);
            }

            return true;
        }

        if (!_capturing || !(isDown || isUp))
        {
            return false;
        }

        if (isDown)
        {
            _swallowedDown.Add(vk);
        }

        // A key-up whose key-down came before the capture (the Enter that clicked "Change") is
        // passed on to the app that saw its key-down; the capture ignores it anyway.
        CaptureKey?.Invoke(vk, isDown);
        return isDown;
    }

    /// <summary>True when this Esc event must be swallowed. The key-up of a swallowed key-down is
    /// swallowed too, so the app never sees half a keystroke.</summary>
    private bool HandleEscape(bool isDown, bool isUp)
    {
        if (isDown && (_escapeDownSwallowed || _shouldSwallowEscape()))
        {
            // Auto-repeat while held arrives as more key-downs; only the first one cancels.
            if (!_escapeDownSwallowed)
            {
                _escapeDownSwallowed = true;
                EscapeSwallowed?.Invoke();
            }

            return true;
        }

        if (isUp && _escapeDownSwallowed)
        {
            _escapeDownSwallowed = false;
            return true;
        }

        return false;
    }

    /// <summary>True when this key-down is the combination's typed key, arriving as the last key
    /// of a held combination: it must not reach the focused app.</summary>
    private bool HandleKeyDown(HoldBinding? binding, int vk, long now)
    {
        if (binding is null)
        {
            return false;
        }

        bool wasEngaged = binding.Tracker.IsEngaged;
        binding.Tracker.OnKeyDown(vk, now);
        if (!wasEngaged && binding.Tracker.IsEngaged)
        {
            StopHoldTimer(binding);

            // A thread timer: WM_TIMER lands in this thread's queue and InputThread's loop
            // dispatches it to OnHoldTimerFor, on this same thread, so the tracker never races.
            uint timerMs = binding.DeferShortcutBreak ? ShortcutBreakDelayMs : binding.HoldThresholdMs;
            if (timerMs > 0)
            {
                binding.Timer = Win32.SetTimer(IntPtr.Zero, UIntPtr.Zero, timerMs, binding.TimerProc);
                _timerOwners[binding.Timer] = binding;
            }

            // A Win key held for a hold must not open the Start menu when it comes up.
            if (!binding.DeferShortcutBreak && (binding.TypedVk != 0 || binding.IsModifierOnlyCombination || binding.UsesWinKey))
            {
                BreakShortcut();
            }
        }

        if (vk == binding.TypedVk && binding.Tracker.IsEngaged)
        {
            binding.TypedDownSwallowed = true;
        }

        // Auto-repeat of the swallowed key arrives as more key-downs.
        return vk == binding.TypedVk && binding.TypedDownSwallowed;
    }

    private bool HandleKeyUp(HoldBinding? binding, int vk, long now)
    {
        if (binding is null)
        {
            return false;
        }

        binding.Tracker.OnKeyUp(vk, now);
        StopHoldTimer(binding);

        // The key-up of a swallowed key-down is swallowed too, so no app sees half a keystroke.
        if (vk == binding.TypedVk && binding.TypedDownSwallowed)
        {
            binding.TypedDownSwallowed = false;
            return true;
        }

        return false;
    }

    /// <summary>Taps the unassigned key so Windows treats the held modifiers as part of a shortcut.
    /// Injected, so this hook ignores it.</summary>
    private static void BreakShortcut()
    {
        try
        {
            SendInputHelper.SendKey(ShortcutBreakerVk);
        }
        catch (InvalidOperationException)
        {
            // Blocked (an elevated window has focus): the combination still works, the Start menu may show.
        }
    }

    private void OnHoldTimerFor(IntPtr hwnd, uint message, UIntPtr idEvent, uint time)
    {
        if (_timerOwners.TryGetValue(idEvent, out HoldBinding? binding))
        {
            StopHoldTimer(binding);
            if (binding.DeferShortcutBreak)
            {
                // Still held and still a hold (no other key spoiled it): Windows must not act on it now.
                if (binding.Tracker.IsEngaged && binding.HoldActive)
                {
                    BreakShortcut();
                }

                return;
            }

            binding.Tracker.CheckHoldThreshold(Environment.TickCount64);
        }
    }

    private void StopHoldTimer(HoldBinding binding)
    {
        if (binding.Timer != UIntPtr.Zero)
        {
            Win32.KillTimer(IntPtr.Zero, binding.Timer);
            _timerOwners.Remove(binding.Timer);
            binding.Timer = UIntPtr.Zero;
        }
    }

    private void StopHoldTimers()
    {
        if (_trigger is not null)
        {
            StopHoldTimer(_trigger);
        }

        if (_rewrite is not null)
        {
            StopHoldTimer(_rewrite);
        }
    }

    public void Dispose()
    {
        StopHoldTimers();
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    /// <summary>One held key set (the Trigger Key, or the Rewrite key): its tracker, its hold
    /// timer and the typed key of a combination. The timer callback is kept here so native code
    /// never holds a collected delegate.</summary>
    private sealed class HoldBinding
    {
        public HoldBinding(TriggerKeyTracker tracker, int typedVk, Win32.TimerProc timerProc, bool deferShortcutBreak, uint holdThresholdMs, bool usesWinKey)
        {
            HoldThresholdMs = holdThresholdMs;
            UsesWinKey = usesWinKey;
            Tracker = tracker;
            TypedVk = typedVk;
            TimerProc = timerProc;
            DeferShortcutBreak = deferShortcutBreak;
        }

        public TriggerKeyTracker Tracker { get; }

        /// <summary>How long the keys must stay down before the hold starts; 0 starts it at the press.</summary>
        public uint HoldThresholdMs { get; }

        public bool UsesWinKey { get; }

        /// <summary>True from the hold's start until it ends or is cancelled.</summary>
        public bool HoldActive { get; set; }

        /// <summary>The combination's one non-modifier key (e.g. the Space of Ctrl+Space), or 0.
        /// While the combination is held it is swallowed, so it never types into the focused app.</summary>
        public int TypedVk { get; }

        public Win32.TimerProc TimerProc { get; }

        /// <summary>True when Windows is kept from acting on the held keys only once the hold begins,
        /// not as soon as they are down.</summary>
        public bool DeferShortcutBreak { get; }

        public bool TypedDownSwallowed { get; set; }

        public UIntPtr Timer { get; set; }

        public bool IsModifierOnlyCombination => TypedVk == 0 && Tracker.IsMultiKey;
    }
}
