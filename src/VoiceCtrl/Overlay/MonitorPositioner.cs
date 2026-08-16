using System.Runtime.InteropServices;
using System.Windows;
using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Overlay;

/// <summary>
/// Computes where to place the overlay bar: bottom-center of the work area of whatever
/// monitor currently holds the foreground window, in WPF DIPs.
/// </summary>
public static class MonitorPositioner
{
    public static Point GetBottomCenterPosition(double windowWidthDip, double windowHeightDip, double bottomMarginDip = 24)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        IntPtr monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new Point(
                (SystemParameters.WorkArea.Width - windowWidthDip) / 2,
                SystemParameters.WorkArea.Height - windowHeightDip - bottomMarginDip);
        }

        double dpiScale = GetDpiScale(monitor);

        // rcWork (not rcMonitor) excludes the taskbar so the bar never overlaps it.
        double workLeftDip = info.rcWork.Left / dpiScale;
        double workTopDip = info.rcWork.Top / dpiScale;
        double workWidthDip = (info.rcWork.Right - info.rcWork.Left) / dpiScale;
        double workHeightDip = (info.rcWork.Bottom - info.rcWork.Top) / dpiScale;

        double x = workLeftDip + (workWidthDip - windowWidthDip) / 2;
        double y = workTopDip + workHeightDip - windowHeightDip - bottomMarginDip;
        return new Point(x, y);
    }

    private static double GetDpiScale(IntPtr monitor)
    {
        int hr = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        return hr == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
    }
}
