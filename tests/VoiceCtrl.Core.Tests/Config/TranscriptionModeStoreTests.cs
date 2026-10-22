using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class TranscriptionModeStoreTests
{
    [Fact]
    public void Current_ReadsTheSettingsStore()
    {
        SettingsStore settings = SettingsWithMode(TranscriptionModePreference.Offline);

        Assert.Equal(TranscriptionModePreference.Offline, new TranscriptionModeStore(settings).Current);
    }

    [Fact]
    public void SetAndSave_WritesTheModeIntoTheSettingsStore()
    {
        SettingsStore settings = SettingsWithMode(TranscriptionModePreference.Auto);
        var store = new TranscriptionModeStore(settings);

        store.SetAndSave(TranscriptionModePreference.Offline);

        Assert.Equal(TranscriptionModePreference.Offline, store.Current);
        Assert.Equal(TranscriptionModePreference.Offline, settings.Current.Transcription.Mode);
    }

    [Theory]
    [InlineData(TranscriptionModePreference.Offline)]
    [InlineData(TranscriptionModePreference.Auto)]
    public void ChangingToOfflineOrAuto_RaisesSwitchedSignal(TranscriptionModePreference target)
    {
        var store = new TranscriptionModeStore(SettingsWithMode(TranscriptionModePreference.Online));
        bool signaled = false;
        store.SwitchedToOfflineOrAuto += () => signaled = true;

        store.SetAndSave(target);

        Assert.True(signaled);
    }

    [Fact]
    public void ChangingToOnline_DoesNotRaiseSwitchedSignal()
    {
        var store = new TranscriptionModeStore(SettingsWithMode(TranscriptionModePreference.Offline));
        bool signaled = false;
        store.SwitchedToOfflineOrAuto += () => signaled = true;

        store.SetAndSave(TranscriptionModePreference.Online);

        Assert.False(signaled);
    }

    [Fact]
    public void ModeUnchanged_DoesNotRaiseSwitchedSignal()
    {
        var store = new TranscriptionModeStore(SettingsWithMode(TranscriptionModePreference.Auto));
        bool signaled = false;
        store.SwitchedToOfflineOrAuto += () => signaled = true;

        store.SetAndSave(TranscriptionModePreference.Auto);

        Assert.False(signaled);
    }

    [Fact]
    public void AModeChangeMadeDirectlyInTheSettingsStore_AlsoRaisesSwitchedSignal()
    {
        // Settings apply live: whoever changes the Mode (tray, Hub, a future bridge handler),
        // the offline model warm-up hears about it.
        SettingsStore settings = SettingsWithMode(TranscriptionModePreference.Online);
        var store = new TranscriptionModeStore(settings);
        bool signaled = false;
        store.SwitchedToOfflineOrAuto += () => signaled = true;

        settings.Update(s => s with { Transcription = s.Transcription with { Mode = TranscriptionModePreference.Offline } });

        Assert.True(signaled);
    }

    [Fact]
    public void AnUnrelatedTranscriptionChange_DoesNotRaiseSwitchedSignal()
    {
        SettingsStore settings = SettingsWithMode(TranscriptionModePreference.Auto);
        var store = new TranscriptionModeStore(settings);
        bool signaled = false;
        store.SwitchedToOfflineOrAuto += () => signaled = true;

        settings.Update(s => s with { Transcription = s.Transcription with { DefaultCleanup = "light" } });

        Assert.False(signaled);
    }

    private static SettingsStore SettingsWithMode(TranscriptionModePreference mode) =>
        SettingsStore.InMemory(new UserSettings { Transcription = new TranscriptionSection { Mode = mode } });
}
