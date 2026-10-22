using System.Diagnostics;
using System.Text;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Input;

/// <summary>
/// Remembers the last app window the user had in front, other than VoiceCtrl's own and Windows'
/// shell (taskbar, desktop, Alt+Tab): the window Paste Again from the Hub pastes into (spec §7).
/// Follows Windows' foreground changes with an out-of-context WinEvent hook, which Windows calls
/// on this (UI) thread's message loop. UI thread only.
/// </summary>
internal sealed class LastAppWindow : IDisposable
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    // Held in a field: the native hook calls it for the app's lifetime, so it must not be collected.
    private readonly Win32.WinEventProc _onForeground;
    private readonly IntPtr _hook;

    public LastAppWindow()
    {
        _onForeground = OnForeground;
        _hook = Win32.SetWinEventHook(
            Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground,
            0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        Remember(NativeMethods.GetForegroundWindow());
    }

    /// <summary>The window, if it still exists; otherwise zero.</summary>
    public IntPtr Window => _window != IntPtr.Zero && Win32.IsWindow(_window) ? _window : IntPtr.Zero;

    private IntPtr _window;

    /// <summary>The display name of the app that owns <paramref name="window"/>: its
    /// FileDescription, or the exe name when that is empty or unreadable (an elevated app).</summary>
    public static string? AppName(IntPtr window)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0)
            {
                return null;
            }

            using Process process = Process.GetProcessById((int)processId);
            string? description = null;
            try
            {
                description = process.MainModule?.FileVersionInfo.FileDescription;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied (elevated or protected process): the exe name will do.
            }

            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description.Trim();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) =>
        Remember(hwnd);

    private void Remember(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == OwnProcessId)
        {
            return;
        }

        var className = new StringBuilder(256);
        if (Win32.GetClassName(hwnd, className, className.Capacity) > 0 && PasteTargetFilter.IsPasteTarget(className.ToString()))
        {
            _window = hwnd;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_hook);
        }
    }
}
