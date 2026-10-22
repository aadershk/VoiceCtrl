using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Where a v3 install might have left its .env. v3 was a zip the user extracted anywhere, and it
/// kept the Gemini key in a .env next to its exe, so the only pointers back to that folder are the
/// ones v3 wrote itself (research 01 §4). Raw values only: <see cref="EnvCandidateFolders"/> does
/// the interpreting, so tests can feed any value without touching the real registry or Start Menu.
/// </summary>
/// <param name="RunningExeFolder">The folder of the exe now running. A dev build finds the repo's
/// .env here.</param>
/// <param name="RunKeyCommand">The HKCU Run\VoiceCtrl value, e.g. "\"C:\...\VoiceCtrl.exe\"".</param>
/// <param name="StartMenuShortcutTarget">The target of the Start Menu VoiceCtrl.lnk, which v3
/// repointed at whichever exe it last ran.</param>
public sealed record V3Breadcrumbs(string? RunningExeFolder, string? RunKeyCommand, string? StartMenuShortcutTarget)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "VoiceCtrl";
    private const string ShortcutName = "VoiceCtrl.lnk";

    /// <summary>Reads the three breadcrumbs from this PC. Read-only; never throws.</summary>
    public static V3Breadcrumbs FromThisMachine() => new(
        AppContext.BaseDirectory,
        ReadRunKeyCommand(),
        ReadShortcutTarget(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName)));

    /// <summary>The folders to look in for a v3 .env, in the spec's order, without duplicates.</summary>
    internal IEnumerable<string> EnvCandidateFolders()
    {
        string?[] folders =
        [
            RunningExeFolder,
            RunKeyCommand is null ? null : FolderOfCommand(RunKeyCommand),
            StartMenuShortcutTarget is null ? null : FolderOfCommand(StartMenuShortcutTarget),
        ];

        return folders
            .OfType<string>()
            .Select(folder => folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The folder of the exe a command line starts: a quoted path with optional arguments after it
    /// (how v3 and v4 write the Run key), or an unquoted path ending in ".exe" with optional
    /// arguments. Null for anything that doesn't name a path.
    /// </summary>
    internal static string? FolderOfCommand(string command)
    {
        string trimmed = command.Trim();
        string exePath;

        if (trimmed.StartsWith('"'))
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0)
            {
                return null;
            }

            exePath = trimmed[1..closingQuote];
        }
        else
        {
            int exeEnd = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            exePath = exeEnd < 0 ? trimmed : trimmed[..(exeEnd + ".exe".Length)];
        }

        if (exePath.Length == 0)
        {
            return null;
        }

        try
        {
            string? folder = Path.GetDirectoryName(exePath);
            return string.IsNullOrEmpty(folder) ? null : folder;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A .lnk file's target path, through the WScript.Shell COM object that ships with
    /// every Windows install (the same one v3 used to write it). Null if the file is missing or
    /// can't be read.</summary>
    internal static string? ReadShortcutTarget(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
        {
            return null;
        }

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return null;
        }

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic shortcut = ((dynamic)shell!).CreateShortcut(shortcutPath);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            SimpleFileLogger.LogError("Could not read the Start Menu shortcut", ex);
            return null;
        }
        finally
        {
            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string? ReadRunKeyCommand()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            SimpleFileLogger.LogError("Could not read the Run key", ex);
            return null;
        }
    }
}
