using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace VoiceCtrl.Tray;

/// <summary>
/// Creates (and keeps current) a Start Menu shortcut so the app can be launched on demand,
/// distinct from AutoStartManager (login autostart) and the Ctrl-double-tap hotkey (which only
/// triggers dictation inside an already-running instance). Uses the WScript.Shell COM object,
/// which ships with every Windows install, instead of a NuGet package, so this costs zero new
/// dependencies.
///
/// Runs on every startup and unconditionally rewrites the shortcut's properties rather than
/// only creating it once. That makes it self-healing: if the exe's icon changes, or the app
/// is reinstalled to a new folder, the next launch fixes the shortcut automatically instead of
/// it silently going stale. The trade-off is that a shortcut the user deleted on purpose comes
/// back on the next launch; for a single fixed-name launcher entry that's the better default.
/// </summary>
public static class StartMenuShortcut
{
    private const string ShortcutName = "VoiceCtrl.lnk";

    public static void EnsureExists()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string shortcutPath = Path.Combine(startMenuDir, ShortcutName);

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                shortcut.IconLocation = $"{exePath},0";
                shortcut.Description = "VoiceCtrl voice dictation";
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceCtrl] Failed to create Start Menu shortcut: {ex}");
        }
    }
}
