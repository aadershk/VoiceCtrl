using System.Runtime.InteropServices;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Overlay;

/// <summary>
/// The Windows-querying half of Overlay placement: finds the monitor and reads its work area and
/// DPI, then hands the numbers to <see cref="OverlayPlacement"/> for the actual math. Results are
/// physical pixels for that monitor, ready for SetWindowPos.
/// </summary>
public static class MonitorPositioner
{
    /// <summary>
    /// The monitor holding the foreground window — the same lookup the pre-fix code used
    /// (<c>GetForegroundWindow</c> then <c>MonitorFromWindow(MONITOR_DEFAULTTONEAREST)</c>), left
    /// unchanged because this fix is about correcting the resulting pixels, not the placement
    /// policy. A caller that gets back a handle <see cref="BottomCenter(IntPtr,double,double,double)"/>
    /// can't describe (in practice, only a null or stale one) should fall back to not moving the
    /// window, exactly as that overload's null return already signals.
    /// </summary>
    public static IntPtr FindActiveMonitor()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        return NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>The monitor under a screen point (physical px), or the nearest one: where a
    /// dragged Overlay is dropped.</summary>
    public static IntPtr FindMonitorAt(int x, int y) =>
        Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, NativeMethods.MONITOR_DEFAULTTONEAREST);

    /// <summary>The monitor a window is on, or the nearest one.</summary>
    public static IntPtr FindMonitorOf(IntPtr window) =>
        NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST);

    /// <summary>
    /// Where the bar goes on <paramref name="monitor"/> at that monitor's own effective DPI.
    /// Null if the monitor can't be described (a zero or stale handle), in which case the caller
    /// should leave the window where it is.
    /// </summary>
    public static PixelRect? BottomCenter(IntPtr monitor, double barWidthDip, double barHeightDip, double bottomMarginDip) =>
        BottomCenter(monitor, GetDpiScale(monitor), barWidthDip, barHeightDip, bottomMarginDip);

    /// <summary>
    /// Where the bar goes on <paramref name="monitor"/> at an explicitly supplied DPI scale. For
    /// the WM_DPICHANGED path, where the DPI Windows has just announced for the window is the one
    /// to lay out for, whatever the monitor query would say mid-transition.
    /// </summary>
    public static PixelRect? BottomCenter(IntPtr monitor, double dpiScale, double barWidthDip, double barHeightDip, double bottomMarginDip)
    {
        if (GetWorkArea(monitor) is not { } workArea)
        {
            return null;
        }

        return OverlayPlacement.BottomCenter(workArea, dpiScale, barWidthDip, barHeightDip, bottomMarginDip);
    }

    /// <summary>The monitor's work area (physical px) and effective DPI scale, or null for a zero
    /// or stale handle.</summary>
    public static (PixelRect WorkArea, double DpiScale)? Describe(IntPtr monitor) =>
        GetWorkArea(monitor) is { } workArea ? (workArea, GetDpiScale(monitor)) : null;

    private static PixelRect? GetWorkArea(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        // rcWork (not rcMonitor) excludes the taskbar so the bar never overlaps it.
        NativeMethods.RECT work = info.rcWork;
        return new PixelRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
    }

    private static double GetDpiScale(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return 1.0;
        }

        int hr = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        return hr == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
    }
}
