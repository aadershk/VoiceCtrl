using System.Runtime.InteropServices;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Input;

/// <summary>
/// The WH_MOUSE_LL hook (spec §2, §7). The Overlay window is always click-through, so this is how
/// its clicks and hover arrive: while the cursor is inside <see cref="HitRect"/> the hook reports
/// enter, move, leave and the primary button through <see cref="Pointer"/>, and swallows button
/// and wheel input there so it never reaches the app underneath. Cursor movement is never
/// swallowed (that would pin the cursor at the pill's edge). While a primary press that began on
/// the pill is held, the pointer counts as inside wherever it goes, so a drag keeps reporting
/// even when it outruns the window following it. Lives on <see cref="InputThread"/>.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_MOUSEHWHEEL = 0x020E;

    private readonly Win32.LowLevelMouseProc _proc;
    private IntPtr _hookHandle;

    // Hook-thread state only.
    private bool _inside;

    /// <summary>Buttons whose press was swallowed, so their release is swallowed too wherever it
    /// happens; a release whose press reached an app is left alone (no stuck drags).</summary>
    private int _swallowedButtons;

    public MouseHook() => _proc = HookCallback;

    // Replaced whole (never mutated), so the hook thread reads it without a lock.
    private volatile ScreenRect? _hitRect;

    /// <summary>The Overlay's hit rectangle in physical screen pixels, or null. Set from any thread.</summary>
    public ScreenRect? HitRect
    {
        get => _hitRect;
        set => _hitRect = value;
    }

    /// <summary>Pointer input over the hit rectangle, in physical screen pixels. Raised on the hook
    /// thread: handlers must only post onwards and return at once (Windows drops a slow hook).</summary>
    public event Action<OverlayPointerKind, int, int>? Pointer;

    public void Install()
    {
        _hookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to install the mouse hook (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handle((int)wParam, Marshal.PtrToStructure<Win32.POINT>(lParam)))
        {
            return 1;
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>Reports the event and says whether to swallow it. MSLLHOOKSTRUCT starts with the
    /// cursor POINT, which is all this needs.</summary>
    private bool Handle(int message, Win32.POINT point)
    {
        bool pressHeld = (_swallowedButtons & ButtonBit(WM_LBUTTONDOWN)) != 0;
        bool inside = pressHeld || (HitRect is { } rect && rect.Contains(point.X, point.Y));
        if (inside != _inside)
        {
            _inside = inside;
            Pointer?.Invoke(inside ? OverlayPointerKind.Enter : OverlayPointerKind.Leave, point.X, point.Y);
        }

        switch (message)
        {
            case WM_MOUSEMOVE:
                if (inside)
                {
                    Pointer?.Invoke(OverlayPointerKind.Move, point.X, point.Y);
                }

                return false;

            case WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN:
                if (!inside)
                {
                    return false;
                }

                _swallowedButtons |= ButtonBit(message);
                if (message == WM_LBUTTONDOWN)
                {
                    Pointer?.Invoke(OverlayPointerKind.Down, point.X, point.Y);
                }

                return true;

            case WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONUP:
                int bit = ButtonBit(message);
                if ((_swallowedButtons & bit) == 0)
                {
                    return false;
                }

                _swallowedButtons &= ~bit;
                if (message == WM_LBUTTONUP)
                {
                    // Reported even outside: the page decides whether it was a click.
                    Pointer?.Invoke(OverlayPointerKind.Up, point.X, point.Y);
                }

                return true;

            case WM_MOUSEWHEEL or WM_MOUSEHWHEEL:
                return inside;

            default:
                return false;
        }
    }

    private static int ButtonBit(int message) => message switch
    {
        WM_LBUTTONDOWN or WM_LBUTTONUP => 1,
        WM_RBUTTONDOWN or WM_RBUTTONUP => 2,
        WM_MBUTTONDOWN or WM_MBUTTONUP => 4,
        _ => 8,
    };

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}

/// <summary>An immutable screen rectangle, physical pixels.</summary>
internal sealed record ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
