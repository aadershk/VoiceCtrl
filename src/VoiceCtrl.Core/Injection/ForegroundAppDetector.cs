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

    /// <summary>The foreground window's title, which names a browser's site ("Inbox - Gmail"). It can
    /// hold an email's subject or a document's name, so it is only ever matched against, never logged.</summary>
    public static string? GetForegroundWindowTitle()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var title = new System.Text.StringBuilder(512);
            return NativeMethods.GetWindowText(hwnd, title, title.Capacity) > 0 ? title.ToString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
