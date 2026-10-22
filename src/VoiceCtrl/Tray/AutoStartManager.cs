using Microsoft.Win32;

namespace VoiceCtrl.Tray;

/// <summary>
/// Start with Windows: the HKCU Run value. Rewritten on every start to match the setting (spec §9),
/// so it always points at the exe that is running now; that self-heals a moved install and the
/// v3 → v4 path change. Uses <see cref="Environment.ProcessPath"/>, since the assembly location is
/// empty in a single-file publish.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiceCtrl";

    /// <summary>The flag the Run value passes, so a login start stays in the tray.</summary>
    public const string AutostartArgument = "--autostart";

    public static void Apply(bool startWithWindows)
    {
        if (startWithWindows)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    /// <summary>Uninstall always removes the value, whatever the setting (spec §9).</summary>
    public static void Remove() => Disable();

    private static void Enable()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        string value = $"\"{exePath}\" {AutostartArgument}";
        if (key.GetValue(ValueName) as string != value)
        {
            key.SetValue(ValueName, value);
        }
    }

    private static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
