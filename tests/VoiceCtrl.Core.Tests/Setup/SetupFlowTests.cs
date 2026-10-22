using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Setup;

public sealed class SetupFlowTests
{
    private const string Key = "AIzaSyB7Qk2mL0pX4vT9sW1cE8rN6yH3f9a";

    private readonly SettingsStore _settings = SettingsStore.InMemory(new UserSettings());
    private readonly SecretsStore _secrets = SecretsStore.InMemory();

    [Fact]
    public void OnlineWithAKey_SavesTheKeyAndOnline()
    {
        SetupFlow.Finish(_settings, _secrets, new SetupChoices(SetupListening.Online, $" {Key} ", StartWithWindows: true));

        Assert.Equal(Key, _secrets.GeminiApiKey);
        Assert.Equal(TranscriptionModePreference.Online, _settings.Current.Transcription.Mode);
        Assert.True(_settings.Current.App.SetupComplete);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OnlineWithTheKeySkipped_SavesAuto(string? key)
    {
        SetupFlow.Finish(_settings, _secrets, new SetupChoices(SetupListening.Online, key, StartWithWindows: true));

        // Auto falls back to Offline while there is no key, as the skip note promises; Online
        // with no key would fail every Dictation.
        Assert.Equal(TranscriptionModePreference.Auto, _settings.Current.Transcription.Mode);
        Assert.Equal(string.Empty, _secrets.GeminiApiKey);
        Assert.True(_settings.Current.App.SetupComplete);
    }

    [Fact]
    public void Offline_SavesOffline_AndIgnoresAKeyTypedBeforeGoingBack()
    {
        SetupFlow.Finish(_settings, _secrets, new SetupChoices(SetupListening.Offline, Key, StartWithWindows: true));

        Assert.Equal(TranscriptionModePreference.Offline, _settings.Current.Transcription.Mode);
        Assert.Equal(string.Empty, _secrets.GeminiApiKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Finish_SavesStartWithWindows(bool on)
    {
        SetupFlow.Finish(_settings, _secrets, new SetupChoices(SetupListening.Online, Key, on));

        Assert.Equal(on, _settings.Current.App.StartWithWindows);
    }

    [Theory]
    [InlineData(SetupListening.Online, false, false)]
    [InlineData(SetupListening.Online, true, true)]
    [InlineData(SetupListening.Offline, false, true)]
    public void Finish_SavesSmartCleanup_OnForOfflineWhateverTheAnswer(SetupListening listening, bool answer, bool expected)
    {
        SetupFlow.Finish(_settings, _secrets, new SetupChoices(listening, Key, StartWithWindows: true, SmartCleanup: answer));

        Assert.Equal(expected, _settings.Current.Transcription.SmartCleanup);
    }

    [Fact]
    public void Finish_KeepsTheOtherSettings()
    {
        _settings.Update(s => s with { App = s.App with { Theme = ThemePreference.Dark }, Transcription = s.Transcription with { DefaultCleanup = "light" } });

        SetupFlow.Finish(_settings, _secrets, new SetupChoices(SetupListening.Online, Key, StartWithWindows: false));

        Assert.Equal(ThemePreference.Dark, _settings.Current.App.Theme);
        Assert.Equal("light", _settings.Current.Transcription.DefaultCleanup);
    }

    [Fact]
    public void ClosingEarly_CountsAsSkipping()
    {
        _settings.Update(s => s with { Transcription = s.Transcription with { Mode = TranscriptionModePreference.Online } });

        SetupFlow.Skip(_settings);

        Assert.Equal(TranscriptionModePreference.Auto, _settings.Current.Transcription.Mode);
        Assert.True(_settings.Current.App.SetupComplete);
        Assert.True(_settings.Current.App.StartWithWindows);
        Assert.Equal(string.Empty, _secrets.GeminiApiKey);
    }

    [Theory]
    [InlineData(KeyValidation.Valid, SetupKeyCheck.Works)]
    [InlineData(KeyValidation.InvalidKey, SetupKeyCheck.DidNotWork)]
    [InlineData(KeyValidation.NetworkError, SetupKeyCheck.NotChecked)]
    [InlineData(KeyValidation.Timeout, SetupKeyCheck.NotChecked)]
    [InlineData(KeyValidation.Unknown, SetupKeyCheck.NotChecked)]
    public void KeyCheck_OnlyARejectionSaysTheKeyDidNotWork(KeyValidation result, SetupKeyCheck expected)
    {
        Assert.Equal(expected, SetupFlow.ToKeyCheck(result));
    }
}
