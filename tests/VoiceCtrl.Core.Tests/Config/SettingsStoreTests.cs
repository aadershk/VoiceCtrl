using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Overlay;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"voicectrl-test-settings-{Guid.NewGuid()}");

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Open_NoFile_WritesTheSeedWithAVersionField()
    {
        var seed = new UserSettings { App = new AppSection { Theme = ThemePreference.Dark } };

        SettingsStore store = SettingsStore.Open(SettingsPath, () => seed);

        Assert.Equal(ThemePreference.Dark, store.Current.App.Theme);
        string json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"theme\": \"dark\"", json);
    }

    [Fact]
    public void Open_ExistingFile_DoesNotCallTheSeed()
    {
        SettingsStore.Open(SettingsPath, () => new UserSettings());

        SettingsStore.Open(SettingsPath, () => throw new InvalidOperationException("seed must not run"));
    }

    [Fact]
    public void Update_EverySection_SurvivesAReopen()
    {
        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());

        store.Update(s => s with
        {
            Dictation = new DictationSection
            {
                Hotkeys = new HotkeySettings
                {
                    HotkeyKey = "Left Alt",
                    TriggerEnabled = true,
                    TriggerMode = HotkeySettings.TriggerModeChord,
                    ChordPrefix = "Left Ctrl",
                    ChordSecondKeyVk = 0x20,
                    ChordSecondKeyLabel = "Space",
                },
            },
            Transcription = new TranscriptionSection { Mode = TranscriptionModePreference.Offline, DefaultCleanup = "light" },
            App = new AppSection { Theme = ThemePreference.Light, StartWithWindows = false, SetupComplete = true },
            Advanced = new AdvancedSection { DoubleTapWindowMs = 250, PromptStyleApps = ["claude", "cursor"] },
        });

        UserSettings reloaded = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current;

        Assert.Equal("Left Alt", reloaded.Dictation.Hotkeys.HotkeyKey);
        Assert.Equal(0x20, reloaded.Dictation.Hotkeys.ChordSecondKeyVk);
        Assert.True(reloaded.Dictation.Hotkeys.IsTriggerReady);
        Assert.Equal(TranscriptionModePreference.Offline, reloaded.Transcription.Mode);
        Assert.Equal("light", reloaded.Transcription.DefaultCleanup);
        Assert.Equal(ThemePreference.Light, reloaded.App.Theme);
        Assert.False(reloaded.App.StartWithWindows);
        Assert.True(reloaded.App.SetupComplete);
        Assert.Equal(250, reloaded.Advanced.DoubleTapWindowMs);
        Assert.Equal(["claude", "cursor"], reloaded.Advanced.PromptStyleApps!);
        Assert.Null(reloaded.Advanced.GeminiModelId);
    }

    [Fact]
    public void EngineRecord_IsOffByDefault_AndSurvivesAReopen()
    {
        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());
        Assert.False(store.Current.Privacy.EngineRecord);

        store.Update(s => s with { Privacy = s.Privacy with { EngineRecord = true } });

        Assert.True(SettingsStore.Open(SettingsPath, () => new UserSettings()).Current.Privacy.EngineRecord);
        Assert.Contains("\"engineRecord\": true", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void KeepDictations_DefaultsToTheLast50_AndIsSavedAsCamelCase()
    {
        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());
        Assert.Equal(KeepDictations.Last50, store.Current.Privacy.KeepDictations);

        store.Update(s => s with { Privacy = s.Privacy with { KeepDictations = KeepDictations.Last24Hours } });

        Assert.Contains("\"keepDictations\": \"last24Hours\"", File.ReadAllText(SettingsPath));
        Assert.Equal(KeepDictations.Last24Hours, SettingsStore.Open(SettingsPath, () => new UserSettings()).Current.Privacy.KeepDictations);
    }

    [Fact]
    public void Update_RaisesTheEventOfEachChangedSectionOnly()
    {
        SettingsStore store = SettingsStore.InMemory(new UserSettings());
        var raised = new List<string>();
        store.DictationChanged += _ => raised.Add("dictation");
        store.TranscriptionChanged += _ => raised.Add("transcription");
        store.OverlayChanged += _ => raised.Add("overlay");
        store.PrivacyChanged += _ => raised.Add("privacy");
        store.AppChanged += _ => raised.Add("app");
        store.AdvancedChanged += _ => raised.Add("advanced");

        store.Update(s => s with
        {
            Transcription = s.Transcription with { Mode = TranscriptionModePreference.Online },
            Advanced = s.Advanced with { ChatStyleApps = ["whatsapp"] },
        });

        Assert.Equal(["transcription", "advanced"], raised);
    }

    [Fact]
    public void Update_PassesTheNewSectionValueAndCurrentIsAlreadyUpdated()
    {
        SettingsStore store = SettingsStore.InMemory(new UserSettings());
        AppSection? received = null;
        ThemePreference? currentDuringEvent = null;
        store.AppChanged += app =>
        {
            received = app;
            currentDuringEvent = store.Current.App.Theme;
        };

        store.Update(s => s with { App = s.App with { Theme = ThemePreference.Dark } });

        Assert.Equal(ThemePreference.Dark, received?.Theme);
        Assert.Equal(ThemePreference.Dark, currentDuringEvent);
    }

    [Fact]
    public void Update_ListsWithEqualContents_DoNotCountAsAChange()
    {
        SettingsStore store = SettingsStore.InMemory(new UserSettings { Advanced = new AdvancedSection { PromptStyleApps = ["claude"] } });
        bool raised = false;
        store.AdvancedChanged += _ => raised = true;

        store.Update(s => s with { Advanced = s.Advanced with { PromptStyleApps = ["claude"] } });

        Assert.False(raised);
    }

    [Fact]
    public void Update_LeavesNoTempFileBehind()
    {
        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());

        store.Update(s => s with { App = s.App with { SetupComplete = true } });

        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"version":1,"app":{"theme":"purple"}}""")]
    [InlineData("""{"version":1,"app":{"theme":7}}""")]
    public void Open_CorruptFile_IsMovedAsideAndReplacedWithDefaults(string corruptContents)
    {
        File.WriteAllText(SettingsPath, corruptContents);

        SettingsStore store = SettingsStore.Open(SettingsPath, () => throw new InvalidOperationException("not a first run"));

        Assert.Equal(ThemePreference.System, store.Current.App.Theme);
        string corruptCopy = Assert.Single(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
        Assert.Equal(corruptContents, File.ReadAllText(corruptCopy));
        Assert.Contains("\"version\": 1", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Open_CorruptFile_KeepsSetupMarkedDone()
    {
        // A settings.json existed, so this isn't a first run: sending the user back through
        // Setup would be worse than the defaults.
        File.WriteAllText(SettingsPath, "{not valid json");

        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());

        Assert.True(store.Current.App.SetupComplete);
    }

    [Fact]
    public void Open_MissingOrNullSections_GetTheirDefaults()
    {
        File.WriteAllText(SettingsPath, """{"version":1,"app":null,"dictation":{"hotkeys":null},"transcription":{"mode":"offline"}}""");

        UserSettings settings = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current;

        Assert.Equal(ThemePreference.System, settings.App.Theme);
        Assert.Equal(HotkeyKeyCatalog.DefaultHotkeyKey, settings.Dictation.Hotkeys.HotkeyKey);
        Assert.Equal(TranscriptionModePreference.Offline, settings.Transcription.Mode);
        Assert.Equal("standard", settings.Transcription.DefaultCleanup);
        Assert.NotNull(settings.Overlay);
        Assert.NotNull(settings.Advanced);
    }

    [Fact]
    public void Update_MicrophoneAndPasteAgain_SurviveAReopen()
    {
        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());

        store.Update(s => s with
        {
            Dictation = s.Dictation with
            {
                MicrophoneId = "{0.0.1.00000000}.{abc}",
                MicrophoneName = "Headset (Jabra Evolve2 65)",
                PasteAgain = new PasteAgainKey { Modifiers = ["Ctrl", "Alt"], KeyVk = 0x42, KeyLabel = "B" },
            },
        });
        DictationSection reopened = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current.Dictation;

        Assert.Equal("{0.0.1.00000000}.{abc}", reopened.MicrophoneId);
        Assert.Equal("Headset (Jabra Evolve2 65)", reopened.MicrophoneName);
        Assert.Equal(["Ctrl", "Alt", "B"], reopened.PasteAgain.Chips);
    }

    [Fact]
    public void Update_DockAlwaysShowAndSoundCues_SurviveAReopen_AsNamesInTheFile()
    {
        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());

        store.Update(s => s with
        {
            Dictation = s.Dictation with { SoundCues = true },
            Overlay = new OverlaySection { Dock = OverlayDock.Right, AlwaysShow = true },
        });
        UserSettings reopened = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current;

        Assert.True(reopened.Dictation.SoundCues);
        Assert.Equal(new OverlaySection { Dock = OverlayDock.Right, AlwaysShow = true }, reopened.Overlay);
        Assert.Contains("\"dock\": \"right\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Open_FileWrittenBeforeTheOverlaySlices_GetsBottomHiddenAndSilent()
    {
        File.WriteAllText(SettingsPath, """{"version":1,"dictation":{},"overlay":{}}""");

        UserSettings settings = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current;

        Assert.Equal(OverlayDock.Bottom, settings.Overlay.Dock);
        Assert.False(settings.Overlay.AlwaysShow);
        Assert.False(settings.Dictation.SoundCues);
    }

    [Fact]
    public void Open_FileWrittenBeforePressEnterAndMuteOtherAudio_GetsPressEnterOff_AndMuteOn()
    {
        File.WriteAllText(SettingsPath, """{"version":1,"dictation":{}}""");

        UserSettings settings = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current;

        Assert.False(settings.Dictation.PressEnter);
        Assert.True(settings.Dictation.MuteOtherAudio);
    }

    [Fact]
    public void Update_PressEnterAndMuteOtherAudio_SurviveAReopen()
    {
        SettingsStore store = SettingsStore.Open(SettingsPath, () => new UserSettings());

        store.Update(s => s with { Dictation = s.Dictation with { PressEnter = true, MuteOtherAudio = false } });
        DictationSection reopened = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current.Dictation;

        Assert.True(reopened.PressEnter);
        Assert.False(reopened.MuteOtherAudio);
    }

    [Theory]
    [InlineData("""{"version":1,"dictation":{"pasteAgain":null}}""")]
    [InlineData("""{"version":1,"dictation":{"pasteAgain":{"modifiers":["Shift"],"keyVk":86,"keyLabel":"V"}}}""")]
    [InlineData("""{"version":1,"dictation":{"pasteAgain":{"modifiers":["Ctrl"],"keyVk":0,"keyLabel":""}}}""")]
    public void Open_MissingOrUnusablePasteAgainKey_GetsTheDefault(string json)
    {
        File.WriteAllText(SettingsPath, json);

        UserSettings settings = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current;

        Assert.Equal(["Ctrl", "Shift", "V"], settings.Dictation.PasteAgain.Chips);
    }

    [Fact]
    public void Open_FileFromANewerVersion_ReadsTheFieldsItKnows()
    {
        File.WriteAllText(SettingsPath, """{"version":9,"app":{"theme":"dark","someFutureField":true},"futureSection":{}}""");

        UserSettings settings = SettingsStore.Open(SettingsPath, () => new UserSettings()).Current;

        Assert.Equal(ThemePreference.Dark, settings.App.Theme);
        Assert.Empty(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
    }

    [Fact]
    public void Open_FileThatCannotBeRead_IsNeverOverwritten()
    {
        const string original = """{"version":1,"app":{"theme":"dark"}}""";
        File.WriteAllText(SettingsPath, original);

        SettingsStore store;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store = SettingsStore.Open(SettingsPath, () => new UserSettings());
        }

        store.Update(s => s with { App = s.App with { Theme = ThemePreference.Light } });

        Assert.Equal(ThemePreference.Light, store.Current.App.Theme);
        Assert.Equal(original, File.ReadAllText(SettingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
