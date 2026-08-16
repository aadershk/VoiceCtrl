using System.Runtime.InteropServices;
using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Injection;

public static class ElevationChecker
{
    /// <summary>
    /// True only if the foreground window's process is elevated and we are not. This is the specific
    /// case where UIPI silently drops our SendInput with no synchronous failure signal, so this
    /// must be checked before attempting injection rather than after.
    /// </summary>
    public static bool IsForegroundWindowElevated()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == 0)
        {
            return false;
        }

        IntPtr targetProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (targetProcess == IntPtr.Zero)
        {
            // Can't inspect it at all (e.g. a protected system process), so assume the worse case
            // so we fall back to manual-paste messaging instead of risking a silently-dropped paste.
            return true;
        }

        try
        {
            return IsElevated(targetProcess) && !IsElevated(NativeMethods.GetCurrentProcess());
        }
        finally
        {
            NativeMethods.CloseHandle(targetProcess);
        }
    }

    private static bool IsElevated(IntPtr processHandle)
    {
        if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TOKEN_QUERY, out IntPtr tokenHandle))
        {
            return false;
        }

        try
        {
            bool ok = NativeMethods.GetTokenInformation(
                tokenHandle,
                NativeMethods.TOKEN_INFORMATION_CLASS.TokenElevation,
                out NativeMethods.TOKEN_ELEVATION elevation,
                Marshal.SizeOf<NativeMethods.TOKEN_ELEVATION>(),
                out _);

            return ok && elevation.TokenIsElevated != 0;
        }
        finally
        {
            NativeMethods.CloseHandle(tokenHandle);
        }
    }
}
