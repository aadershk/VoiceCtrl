using System.IO;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

/// <summary>
/// The one-time v3 migration, driven through <see cref="SettingsStartup"/> the way the app runs it,
/// against a temp data root and fake breadcrumbs, so no test reads the real registry, Start Menu
/// or %LocalAppData%.
/// </summary>
public sealed class V3MigrationTests : IDisposable
{
    private const string V3Key = "AIzaSyV3MigratedKey0000000000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-migration-{Guid.NewGuid()}");

    public V3MigrationTests()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(RunningExeFolder);
        Directory.CreateDirectory(OldInstallFolder);
        Directory.CreateDirectory(ShortcutFolder);
    }

    private string DataRoot => Path.Combine(_root, "LocalAppData", "VoiceCtrl");

    private string RunningExeFolder => Path.Combine(_root, "current");

    private string OldInstallFolder => Path.Combine(_root, "Desktop", "VoiceCtrl v3");

    private string ShortcutFolder => Path.Combine(_root, "Downloads", "VoiceCtrl");

    [Fact]
    public void EnvNextToTheRunningExe_CarriesOverKeyCleanupAndTuning()
    {
        WriteEnv(RunningExeFolder, $"""
            GEMINI_API_KEY={V3Key}
            CLEANUP_LEVEL=aggressive
            DOUBLE_TAP_WINDOW_MS=250
            PROMPT_STYLE_APPS=claude, cursor
            # GEMINI_MODEL_ID=commented-out
            """);

        (SettingsStore settings, SecretsStore secrets) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.Equal(V3Key, secrets.GeminiApiKey);
        Assert.Equal("aggressive", settings.Current.Transcription.DefaultCleanup);
        Assert.Equal(250, settings.Current.Advanced.DoubleTapWindowMs);
        Assert.Equal(["claude", "cursor"], settings.Current.Advanced.PromptStyleApps!);
        Assert.Null(settings.Current.Advanced.GeminiModelId);
        Assert.True(settings.Current.App.SetupComplete);
    }

    [Theory]
    [InlineData("\"{0}\\VoiceCtrl.exe\"")]
    [InlineData("\"{0}\\VoiceCtrl.exe\" --autostart")]
    [InlineData("{0}\\VoiceCtrl.exe")]
    public void EnvInTheRunKeysFolder_IsFound(string runKeyTemplate)
    {
        WriteEnv(OldInstallFolder, $"GEMINI_API_KEY={V3Key}");
        string runKey = string.Format(runKeyTemplate, OldInstallFolder);

        (SettingsStore settings, SecretsStore secrets) = Open(new V3Breadcrumbs(RunningExeFolder, runKey, null));

        Assert.Equal(V3Key, secrets.GeminiApiKey);
        Assert.True(settings.Current.App.SetupComplete);
    }

    [Fact]
    public void EnvInTheStartMenuShortcutsTargetFolder_IsFound()
    {
        WriteEnv(ShortcutFolder, $"GEMINI_API_KEY={V3Key}");

        (SettingsStore settings, SecretsStore secrets) = Open(
            new V3Breadcrumbs(RunningExeFolder, null, Path.Combine(ShortcutFolder, "VoiceCtrl.exe")));

        Assert.Equal(V3Key, secrets.GeminiApiKey);
        Assert.True(settings.Current.App.SetupComplete);
    }

    [Fact]
    public void SeveralEnvFiles_TheRunningExesFolderWinsThenTheRunKeyThenTheShortcut()
    {
        WriteEnv(OldInstallFolder, "GEMINI_API_KEY=from-run-key");
        WriteEnv(ShortcutFolder, "GEMINI_API_KEY=from-shortcut");
        var breadcrumbs = new V3Breadcrumbs(
            RunningExeFolder, $"\"{OldInstallFolder}\\VoiceCtrl.exe\"", Path.Combine(ShortcutFolder, "VoiceCtrl.exe"));

        (_, SecretsStore secrets) = Open(breadcrumbs);

        Assert.Equal("from-run-key", secrets.GeminiApiKey);
    }

    [Fact]
    public void V3DataFiles_CarryOverModeHotkeysAndTheme()
    {
        File.WriteAllText(Path.Combine(DataRoot, "prefs.json"), """{"TranscriptionMode":"Online"}""");
        File.WriteAllText(Path.Combine(DataRoot, "theme.json"), """{"Theme":"Dark"}""");
        File.WriteAllText(Path.Combine(DataRoot, "hotkey.json"), """
            {
              "HotkeyKey": "Left Alt",
              "TriggerEnabled": true,
              "TriggerMode": "Single",
              "TriggerKey": "F5",
              "ChordPrefix": null,
              "ChordSecondKeyVk": null,
              "ChordSecondKeyLabel": null,
              "IsTriggerReady": true
            }
            """);

        (SettingsStore settings, _) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.Equal(TranscriptionModePreference.Online, settings.Current.Transcription.Mode);
        Assert.Equal(ThemePreference.Dark, settings.Current.App.Theme);
        HotkeySettings hotkeys = settings.Current.Dictation.Hotkeys;
        Assert.Equal("Left Alt", hotkeys.HotkeyKey);
        Assert.Equal("F5", hotkeys.TriggerKey);
        Assert.True(hotkeys.IsTriggerReady);
        Assert.True(settings.Current.App.SetupComplete);
    }

    [Fact]
    public void V3User_WhoNeverSetATriggerKey_GetsTheNewDefaults()
    {
        File.WriteAllText(Path.Combine(DataRoot, "hotkey.json"), """
            {
              "HotkeyKey": "Ctrl",
              "TriggerEnabled": false,
              "TriggerMode": "Single",
              "TriggerKey": null
            }
            """);

        (SettingsStore settings, _) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        HotkeySettings hotkeys = settings.Current.Dictation.Hotkeys;
        Assert.Equal(["Left Ctrl", "Left Win"], hotkeys.TriggerChips);
        Assert.Equal(["Left Ctrl", "Left Shift"], hotkeys.RewriteChips);
    }

    [Fact]
    public void V3User_WhoSetATriggerKeyAndSwitchedItOff_KeepsItOff()
    {
        File.WriteAllText(Path.Combine(DataRoot, "hotkey.json"), """
            {
              "HotkeyKey": "Ctrl",
              "TriggerEnabled": false,
              "TriggerMode": "Single",
              "TriggerKey": "F5"
            }
            """);

        (SettingsStore settings, _) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.False(settings.Current.Dictation.Hotkeys.IsTriggerReady);
    }

    [Fact]
    public void PrefsJsonMode_WinsOverTheEnvMode()
    {
        WriteEnv(RunningExeFolder, "TRANSCRIPTION_MODE=Offline");
        File.WriteAllText(Path.Combine(DataRoot, "prefs.json"), """{"TranscriptionMode":"Online"}""");

        (SettingsStore settings, _) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.Equal(TranscriptionModePreference.Online, settings.Current.Transcription.Mode);
    }

    [Theory]
    [InlineData("TRANSCRIPTION_MODE=Offline", TranscriptionModePreference.Offline)]
    [InlineData("TRANSCRIPTION_MODE=offline", TranscriptionModePreference.Offline)]
    [InlineData("TRANSCRIPTION_MODE=Online", TranscriptionModePreference.Auto)]
    [InlineData("TRANSCRIPTION_MODE=sometimes", TranscriptionModePreference.Auto)]
    [InlineData("", TranscriptionModePreference.Auto)]
    public void WithoutPrefsJson_OnlyAnExplicitOfflineInEnvStaysOffline(string envLine, TranscriptionModePreference expected)
    {
        // v3's own seeding rule: Offline may be a privacy choice (never send audio to Google), so
        // it's kept; anything else widens to Auto, which behaves as Online whenever there's internet.
        WriteEnv(RunningExeFolder, envLine);

        (SettingsStore settings, _) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.Equal(expected, settings.Current.Transcription.Mode);
    }

    [Fact]
    public void ADataFileButNoEnv_StillSkipsSetupWithoutAKey()
    {
        File.WriteAllText(Path.Combine(DataRoot, "history.json"), "[]");

        (SettingsStore settings, SecretsStore secrets) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.True(settings.Current.App.SetupComplete);
        Assert.Equal(string.Empty, secrets.GeminiApiKey);
    }

    [Fact]
    public void NothingFromV3_IsAFreshInstall()
    {
        (SettingsStore settings, SecretsStore secrets) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.False(settings.Current.App.SetupComplete);
        Assert.Equal(new UserSettings().Transcription with { Provider = OnlineProvider.Gemini }, settings.Current.Transcription);
        Assert.Equal(string.Empty, secrets.GeminiApiKey);
        Assert.True(File.Exists(Path.Combine(DataRoot, "settings.json")));
        Assert.False(File.Exists(Path.Combine(DataRoot, "secrets.bin")));
    }

    [Theory]
    [InlineData("your-api-key-here")]
    [InlineData("")]
    public void APlaceholderOrBlankKey_IsNotMigrated(string key)
    {
        WriteEnv(RunningExeFolder, $"GEMINI_API_KEY={key}");

        (_, SecretsStore secrets) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.Equal(string.Empty, secrets.GeminiApiKey);
    }

    [Fact]
    public void UnparseableTuningValues_AreLeftAtTheirDefaults()
    {
        WriteEnv(RunningExeFolder, """
            DOUBLE_TAP_WINDOW_MS=not-a-number
            COMPRESS_UPLOAD=maybe
            GEMINI_MODEL_ID=
            """);

        (SettingsStore settings, _) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.Null(settings.Current.Advanced.DoubleTapWindowMs);
        Assert.Null(settings.Current.Advanced.CompressUpload);
        Assert.Null(settings.Current.Advanced.GeminiModelId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartWithWindows_FollowsWhetherV3sRunKeyWasThere(bool runKeyPresent)
    {
        WriteEnv(OldInstallFolder, $"GEMINI_API_KEY={V3Key}");
        var breadcrumbs = new V3Breadcrumbs(
            RunningExeFolder,
            runKeyPresent ? $"\"{OldInstallFolder}\\VoiceCtrl.exe\"" : null,
            Path.Combine(OldInstallFolder, "VoiceCtrl.exe"));

        (SettingsStore settings, _) = Open(breadcrumbs);

        Assert.Equal(runKeyPresent, settings.Current.App.StartWithWindows);
    }

    [Fact]
    public void EveryV3File_IsLeftUntouched()
    {
        string envPath = WriteEnv(OldInstallFolder, $"GEMINI_API_KEY={V3Key}\nTRANSCRIPTION_MODE=Offline");
        string prefsPath = Path.Combine(DataRoot, "prefs.json");
        string hotkeyPath = Path.Combine(DataRoot, "hotkey.json");
        string themePath = Path.Combine(DataRoot, "theme.json");
        File.WriteAllText(prefsPath, """{"TranscriptionMode":"Offline"}""");
        File.WriteAllText(hotkeyPath, """{"HotkeyKey":"Left Alt"}""");
        File.WriteAllText(themePath, """{"Theme":"Light"}""");
        string[] v3Files = [envPath, prefsPath, hotkeyPath, themePath];
        var before = v3Files.ToDictionary(path => path, Snapshot);

        Open(new V3Breadcrumbs(RunningExeFolder, $"\"{OldInstallFolder}\\VoiceCtrl.exe\"", null));

        foreach (string path in v3Files)
        {
            Assert.Equal(before[path], Snapshot(path));
        }
    }

    [Fact]
    public void TheMigrationRunsOnlyOnce()
    {
        string prefsPath = Path.Combine(DataRoot, "prefs.json");
        File.WriteAllText(prefsPath, """{"TranscriptionMode":"Offline"}""");
        var breadcrumbs = new V3Breadcrumbs(RunningExeFolder, null, null);
        (SettingsStore first, _) = Open(breadcrumbs);
        first.Update(s => s with { Transcription = s.Transcription with { Mode = TranscriptionModePreference.Auto } });

        (SettingsStore second, _) = SettingsStartup.Open(
            DataRoot, () => throw new InvalidOperationException("breadcrumbs must not be read again"));

        Assert.Equal(TranscriptionModePreference.Auto, second.Current.Transcription.Mode);
    }

    [Fact]
    public void AKeyAlreadyInSecrets_IsNotReplacedByAnOlderV3Key()
    {
        SecretsStore.Load(Path.Combine(DataRoot, "secrets.bin")).SetGeminiApiKey("key-saved-by-v4");
        WriteEnv(RunningExeFolder, $"GEMINI_API_KEY={V3Key}");

        (_, SecretsStore secrets) = Open(new V3Breadcrumbs(RunningExeFolder, null, null));

        Assert.Equal("key-saved-by-v4", secrets.GeminiApiKey);
    }

    [Theory]
    [InlineData("\"C:\\Old Folder\\VoiceCtrl.exe\" --autostart", "C:\\Old Folder")]
    [InlineData("\"C:\\Old Folder\\VoiceCtrl.exe\"", "C:\\Old Folder")]
    [InlineData("C:\\Old Folder\\VoiceCtrl.exe --autostart", "C:\\Old Folder")]
    [InlineData("C:\\Old\\VoiceCtrl.EXE", "C:\\Old")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\"unterminated", null)]
    public void RunKeyCommand_ResolvesToTheExesFolder(string command, string? expectedFolder)
    {
        Assert.Equal(expectedFolder, V3Breadcrumbs.FolderOfCommand(command));
    }

    [Fact]
    public void ReadShortcutTarget_ReadsARealShortcutFile()
    {
        string target = Path.Combine(ShortcutFolder, "VoiceCtrl.exe");
        File.WriteAllText(target, string.Empty);
        string shortcutPath = Path.Combine(_root, "VoiceCtrl.lnk");
        CreateShortcut(shortcutPath, target);

        Assert.Equal(target, V3Breadcrumbs.ReadShortcutTarget(shortcutPath), ignoreCase: true);
        Assert.Null(V3Breadcrumbs.ReadShortcutTarget(Path.Combine(_root, "missing.lnk")));
    }

    private (SettingsStore Settings, SecretsStore Secrets) Open(V3Breadcrumbs breadcrumbs) =>
        SettingsStartup.Open(DataRoot, () => breadcrumbs);

    private static string WriteEnv(string folder, string contents)
    {
        string path = Path.Combine(folder, ".env");
        File.WriteAllText(path, contents);
        return path;
    }

    private static (string Contents, DateTime LastWrite) Snapshot(string path) =>
        (File.ReadAllText(path), File.GetLastWriteTimeUtc(path));

    private static void CreateShortcut(string shortcutPath, string target)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = target;
        shortcut.Save();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
