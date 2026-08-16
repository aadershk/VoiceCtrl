using Microsoft.Win32;

namespace VoiceCtrl.Tray;

/// <summary>
/// Registers/unregisters the app under HKCU Run so it launches at login. Uses
/// Environment.ProcessPath rather than Assembly.GetExecutingAssembly().Location, since the latter
/// returns empty under PublishSingleFile=true, which would silently break autostart only in
/// the published build.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiceCtrl";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existing && existing.Length > 0;
    }

    public static void Enable()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
