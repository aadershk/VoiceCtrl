using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Injection;

public static class ForegroundAppDetector
{
    public static string? GetForegroundProcessName()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0)
            {
                return null;
            }

            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
