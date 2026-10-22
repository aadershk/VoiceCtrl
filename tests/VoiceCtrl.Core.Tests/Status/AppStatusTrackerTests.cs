using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Status;
using Xunit;

namespace VoiceCtrl.Core.Tests.Status;

public class AppStatusTrackerTests
{
    private readonly FakeMicrophones _microphones = new();
    private readonly ApiKeyHealth _keyHealth = new();

    [Fact]
    public void Starts_Ready_WithTheHotkeyForTheHint()
    {
        AppStatusTracker tracker = CreateTracker(hotkey: "Right Ctrl");

        Assert.Equal(
            new AppStatus(AppStatusKind.Ready, "Right Ctrl", Paused: false, OfflineFallback: false, TalkKeys: ["Left Ctrl", "Left Win"]),
            tracker.Current);
    }

    [Fact]
    public void TalkKeys_FollowTheTriggerKey_AndAreNullOnceRemoved()
    {
        SettingsStore settings = Settings(TranscriptionModePreference.Auto, "Ctrl");
        AppStatusTracker tracker = new(settings, _microphones, _keyHealth);
        var seen = new List<AppStatus>();
        tracker.Changed += seen.Add;

        settings.Update(s => s with { Dictation = s.Dictation with { Hotkeys = s.Dictation.Hotkeys with { TriggerRemoved = true } } });

        Assert.Null(tracker.Current.TalkKeys);
        Assert.Single(seen);
    }

    [Fact]
    public void NoMicrophone_WhenNoCaptureDeviceIsPresent()
    {
        _microphones.HasMicrophone = false;

        Assert.Equal(AppStatusKind.NoMicrophone, CreateTracker().Current.Kind);
    }

    [Theory]
    [InlineData(TranscriptionModePreference.Auto, true)]
    [InlineData(TranscriptionModePreference.Online, false)]
    public void ApiKeyNotWorking_WhenGeminiRefusedTheKey_SaysWhetherOfflineStandsIn(TranscriptionModePreference mode, bool offlineFallback)
    {
        _keyHealth.ReportRejected();

        AppStatus status = CreateTracker(mode: mode).Current;

        Assert.Equal(AppStatusKind.ApiKeyNotWorking, status.Kind);
        Assert.Equal(offlineFallback, status.OfflineFallback);
    }

    [Fact]
    public void RefusedKey_DoesNotMatterInOfflineMode()
    {
        _keyHealth.ReportRejected();

        Assert.Equal(AppStatusKind.Ready, CreateTracker(mode: TranscriptionModePreference.Offline).Current.Kind);
    }

    [Fact]
    public void Paused_WhenPausedAndNothingIsWrong()
    {
        AppStatusTracker tracker = CreateTracker();

        tracker.SetPaused(true);

        Assert.Equal(AppStatusKind.Paused, tracker.Current.Kind);
        Assert.True(tracker.Current.Paused);
    }

    [Fact]
    public void Problems_OutrankPaused_ButPausedIsStillReported()
    {
        AppStatusTracker tracker = CreateTracker();
        tracker.SetPaused(true);

        _microphones.Unplug();

        Assert.Equal(AppStatusKind.NoMicrophone, tracker.Current.Kind);
        Assert.True(tracker.Current.Paused);
    }

    [Fact]
    public void NoMicrophone_OutranksARefusedKey()
    {
        _keyHealth.ReportRejected();
        _microphones.HasMicrophone = false;

        Assert.Equal(AppStatusKind.NoMicrophone, CreateTracker().Current.Kind);
    }

    [Fact]
    public void Changed_IsRaisedForEachSourceWithTheNewStatus()
    {
        SettingsStore settings = Settings(TranscriptionModePreference.Auto, "Ctrl");
        AppStatusTracker tracker = new(settings, _microphones, _keyHealth);
        var seen = new List<AppStatus>();
        tracker.Changed += seen.Add;

        _microphones.Unplug();
        _microphones.PlugIn();
        _keyHealth.ReportRejected();
        settings.Update(s => s with { Transcription = s.Transcription with { Mode = TranscriptionModePreference.Offline } });
        settings.Update(s => s with { Dictation = s.Dictation with { Hotkeys = s.Dictation.Hotkeys with { HotkeyKey = "Alt" } } });
        tracker.SetPaused(true);

        Assert.Equal(
            [AppStatusKind.NoMicrophone, AppStatusKind.Ready, AppStatusKind.ApiKeyNotWorking, AppStatusKind.Ready, AppStatusKind.Ready, AppStatusKind.Paused],
            seen.Select(s => s.Kind));
        Assert.Equal("Alt", seen[^1].Hotkey);
    }

    [Fact]
    public void Changed_IsNotRaisedWhenNothingVisibleChanged()
    {
        AppStatusTracker tracker = CreateTracker();
        int raised = 0;
        tracker.Changed += _ => raised++;

        tracker.SetPaused(false);
        _keyHealth.ReportAccepted();
        _microphones.PlugIn();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Dispose_StopsFollowingTheSources()
    {
        AppStatusTracker tracker = CreateTracker();
        int raised = 0;
        tracker.Changed += _ => raised++;

        tracker.Dispose();
        _microphones.Unplug();
        _keyHealth.ReportRejected();

        Assert.Equal(0, raised);
    }

    private AppStatusTracker CreateTracker(TranscriptionModePreference mode = TranscriptionModePreference.Auto, string hotkey = "Ctrl") =>
        new(Settings(mode, hotkey), _microphones, _keyHealth);

    private static SettingsStore Settings(TranscriptionModePreference mode, string hotkey) =>
        SettingsStore.InMemory(new UserSettings
        {
            Transcription = new TranscriptionSection { Mode = mode },
            Dictation = new DictationSection { Hotkeys = new HotkeySettings { HotkeyKey = hotkey } },
        });

    private sealed class FakeMicrophones : IMicrophonePresence
    {
        public bool HasMicrophone { get; set; } = true;

        public event Action? Changed;

        public void Unplug()
        {
            HasMicrophone = false;
            Changed?.Invoke();
        }

        public void PlugIn()
        {
            HasMicrophone = true;
            Changed?.Invoke();
        }
    }
}
