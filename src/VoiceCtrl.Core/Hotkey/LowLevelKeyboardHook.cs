using System.Runtime.InteropServices;
using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Installs a WH_KEYBOARD_LL hook tracking Left and Right Ctrl double-taps. Must be installed
/// from a thread that runs a Win32 message loop (the WPF Dispatcher thread qualifies).
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    // Kept as a field, not a local/lambda: if this delegate were GC-eligible while native
    // code still held the function pointer, the hook would break unpredictably at collection.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly CtrlKeyTracker _tracker;
    private IntPtr _hookHandle = IntPtr.Zero;

    public event Action? DoubleTapDetected
    {
        add => _tracker.DoubleTapDetected += value;
        remove => _tracker.DoubleTapDetected -= value;
    }

    /// <summary>
    /// Raised on every real Escape keydown, anywhere in Windows. Deliberately unfiltered here:
    /// this class has no idea whether a dictation is running, so it reports the key and lets the
    /// subscriber decide. The subscriber's check must therefore be cheap, since this fires for
    /// every Esc the user ever presses. Escape is never swallowed, see <see cref="HookCallback"/>.
    /// </summary>
    public event Action? CancelRequested;

    public LowLevelKeyboardHook(int doubleTapWindowMs)
    {
        _proc = HookCallback;
        _tracker = new CtrlKeyTracker(doubleTapWindowMs, [NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL]);
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

            // Filtering LLKHF_INJECTED runs before any VK-specific logic, for the exact-match and
            // Bluetooth-fallback resolution alike. It's what stops our own synthetic Ctrl+V paste
            // (sent with VK_LCONTROL) from ever being misread as a Left-Ctrl hotkey tap. Now the
            // only thing that does, since VK identity alone no longer separates "our own paste"
            // from "a real Left-Ctrl press" the way it did when only Right Ctrl was tracked.
            if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                int message = wParam.ToInt32();
                int? trackedVk = TrackedCtrlKeyResolver.Resolve(data.vkCode, data.flags);

                if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    if (data.vkCode == NativeMethods.VK_ESCAPE)
                    {
                        CancelRequested?.Invoke();
                    }

                    // Still reported to the tracker afterwards, and as an untracked key: Escape
                    // pressed while a Ctrl is held is Ctrl+Esc (the Start menu), which has to
                    // poison that Ctrl the same as any other chord would.
                    _tracker.OnKeyDown(trackedVk, Environment.TickCount64);
                }
                else if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    _tracker.OnKeyUp(trackedVk);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
