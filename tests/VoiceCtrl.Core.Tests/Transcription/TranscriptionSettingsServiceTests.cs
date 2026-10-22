using System.IO;
using System.Text.Json;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Transcription;

public sealed class TranscriptionSettingsServiceTests : IDisposable
{
    private const string GoodKey = "AIzaSyD-1234567890abcdefghijklmnop3f9a";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-tsettings-{Guid.NewGuid():N}");
    private readonly SettingsStore _settings = SettingsStore.InMemory(new UserSettings());
    private readonly SecretsStore _secrets = SecretsStore.InMemory();
    private readonly ApiKeyHealth _health = new();
    private readonly List<string> _validated = [];
    private KeyValidation _answer = KeyValidation.Valid;
    private readonly TranscriptionSettingsService _service;
    private readonly List<string> _smartDownloads = [];
    private int _changes;

    public TranscriptionSettingsServiceTests()
    {
        var packs = new OfflinePackManager(_settings, _root, (_, _, _) => Task.CompletedTask, () => Task.CompletedTask, () => TimeSpan.Zero);
        var smartPacks = new SmartCleanupPackManager(_settings, _root, (model, _, _) =>
        {
            _smartDownloads.Add(model.Directory);
            return Task.CompletedTask;
        }, () => Task.CompletedTask, () => TimeSpan.Zero);
        _service = new TranscriptionSettingsService(_settings, _secrets, _health, packs, smartPacks, (_, key, _) =>
        {
            _validated.Add(key);
            return Task.FromResult(new KeyCheckOutcome(_answer));
        });
        _service.Changed += () => _changes++;
    }

    [Fact]
    public void ByDefault_ItIsAutoStandardWithNoKeyAndEnglishOnly()
    {
        TranscriptionSettingsView view = _service.View();

        Assert.Equal(TranscriptionModePreference.Auto, view.Mode);
        Assert.Equal("standard", view.DefaultCleanup);
        Assert.Equal(ApiKeyState.None, view.KeyState);
        Assert.Null(view.KeyMask);
        Assert.Equal(OfflinePack.None, view.InstalledPack);
        Assert.Null(view.DownloadingPack);
        Assert.Equal([OfflinePack.European, OfflinePack.AllLanguages], view.Packs.Select(p => p.Pack));
    }

    [Fact]
    public void ByDefault_SmartCleanupIsOnWithTheBetterModelAndNothingDownloaded()
    {
        TranscriptionSettingsView view = _service.View();

        Assert.True(view.SmartCleanup);
        Assert.Equal(SmartCleanupTier.Better, view.SmartCleanupTier);
        Assert.Null(view.SmartCleanupInstalled);
        Assert.Null(view.SmartCleanupDownloading);
        Assert.Equal([SmartCleanupTier.Standard, SmartCleanupTier.Better], view.SmartCleanupPacks.Select(p => p.Tier));
    }

    [Fact]
    public async Task SwitchingSmartCleanupOff_IsSavedAndStartsNoDownload()
    {
        await _service.SetSmartCleanup(false);

        Assert.False(_settings.Current.Transcription.SmartCleanup);
        Assert.Empty(_smartDownloads);
    }

    [Fact]
    public async Task SwitchingSmartCleanupOn_WithNoModelOnDisk_DownloadsTheChosenOne()
    {
        await _service.SetSmartCleanup(false);

        await _service.SetSmartCleanup(true);

        Assert.True(_settings.Current.Transcription.SmartCleanup);
        Assert.Equal(["llm-qwen3-4b"], _smartDownloads);
    }

    [Fact]
    public void TheMeasuredPackFacts_AreWhatTheRowStates()
    {
        IReadOnlyList<OfflinePackInfo> packs = _service.View().Packs;

        OfflinePackInfo european = packs.Single(p => p.Pack == OfflinePack.European);
        OfflinePackInfo all = packs.Single(p => p.Pack == OfflinePack.AllLanguages);
        Assert.Equal(25, european.Languages);
        Assert.False(european.Slower);
        Assert.InRange(european.Bytes, 660_000_000, 680_000_000);
        Assert.True(all.Slower);
        Assert.InRange(all.Bytes, 1_030_000_000, 1_040_000_000);
    }

    [Fact]
    public void SetMode_IsSavedAndAnnounced()
    {
        _service.SetMode(TranscriptionModePreference.Offline);

        Assert.Equal(TranscriptionModePreference.Offline, _settings.Current.Transcription.Mode);
        Assert.Equal(TranscriptionModePreference.Offline, _service.View().Mode);
        Assert.True(_changes > 0);
    }

    [Fact]
    public void SetMode_RefusesAnUnknownMode()
    {
        Assert.Throws<ArgumentException>(() => _service.SetMode((TranscriptionModePreference)42));
    }

    [Theory]
    [InlineData("verbatim")]
    [InlineData("light")]
    [InlineData("standard")]
    [InlineData("aggressive")]
    public void SetDefaultCleanup_SavesEachStoredLevel(string level)
    {
        _service.SetDefaultCleanup(level);

        Assert.Equal(level, _settings.Current.Transcription.DefaultCleanup);
    }

    [Fact]
    public void SetDefaultCleanup_RefusesANameNoLevelHas_AndKeepsTheOldOne()
    {
        _service.SetDefaultCleanup("light");

        Assert.Throws<ArgumentException>(() => _service.SetDefaultCleanup("Thorough"));
        Assert.Equal("light", _settings.Current.Transcription.DefaultCleanup);
    }

    [Fact]
    public async Task AKeyGeminiAccepts_IsSaved_AndTheRowSaysItWorks()
    {
        SetupKeyCheck check = await _service.SaveKeyAsync($"  {GoodKey}  ");

        Assert.Equal(SetupKeyCheck.Works, check);
        Assert.Equal(GoodKey, _secrets.GeminiApiKey);
        Assert.Equal([GoodKey], _validated);
        TranscriptionSettingsView view = _service.View();
        Assert.Equal(ApiKeyState.Works, view.KeyState);
        Assert.Equal("AIza ···· 3f9a", view.KeyMask);
    }

    [Fact]
    public async Task AKeyGeminiRefuses_IsNotSaved_AndTheWorkingKeyStays()
    {
        await _service.SaveKeyAsync(GoodKey);
        _answer = KeyValidation.InvalidKey;

        SetupKeyCheck check = await _service.SaveKeyAsync("AIzaTypoTypoTypoTypoTypoTypoTypoTypo");

        Assert.Equal(SetupKeyCheck.DidNotWork, check);
        Assert.Equal(GoodKey, _secrets.GeminiApiKey);
        Assert.Equal(ApiKeyState.Works, _service.View().KeyState);
    }

    [Fact]
    public async Task WhenTheCheckCannotRun_TheKeyIsSavedAnyway_ButNotClaimedToWork()
    {
        _answer = KeyValidation.NetworkError;

        SetupKeyCheck check = await _service.SaveKeyAsync(GoodKey);

        Assert.Equal(SetupKeyCheck.NotChecked, check);
        Assert.Equal(GoodKey, _secrets.GeminiApiKey);
        Assert.Equal(ApiKeyState.Saved, _service.View().KeyState);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyKey_IsRefusedWithoutAskingGemini(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveKeyAsync(key));

        Assert.Empty(_validated);
    }

    [Fact]
    public async Task AnOverlongKey_IsRefusedWithoutAskingGemini()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveKeyAsync(new string('a', 201)));

        Assert.Empty(_validated);
    }

    [Fact]
    public async Task RemovingTheKey_ClearsIt()
    {
        await _service.SaveKeyAsync(GoodKey);

        _service.RemoveKey();

        Assert.Equal("", _secrets.GeminiApiKey);
        Assert.Equal(ApiKeyState.None, _service.View().KeyState);
        Assert.Null(_service.View().KeyMask);
    }

    [Fact]
    public async Task WhenGeminiRefusesTheSavedKeyLater_TheRowSaysSo_UntilANewKeyIsSaved()
    {
        await _service.SaveKeyAsync(GoodKey);

        _health.ReportRejected();
        Assert.Equal(ApiKeyState.Rejected, _service.View().KeyState);

        await _service.SaveKeyAsync(GoodKey.Replace("3f9a", "7c1e"));
        Assert.Equal(ApiKeyState.Works, _service.View().KeyState);
    }

    [Fact]
    public async Task ASavedKeyFromAnEarlierRun_IsCheckedOnce_AndThenSaysItWorks()
    {
        _secrets.SetGeminiApiKey(GoodKey);
        Assert.Equal(ApiKeyState.Saved, _service.View().KeyState);

        await _service.VerifySavedKeyAsync();
        await _service.VerifySavedKeyAsync();

        Assert.Equal(ApiKeyState.Works, _service.View().KeyState);
        Assert.Single(_validated);
    }

    [Fact]
    public async Task ASavedKeyGeminiRefuses_IsReportedAsRejected_ForTheStatusCardToo()
    {
        _secrets.SetGeminiApiKey(GoodKey);
        _answer = KeyValidation.InvalidKey;

        await _service.VerifySavedKeyAsync();

        Assert.Equal(ApiKeyState.Rejected, _service.View().KeyState);
        Assert.True(_health.IsRejected);
    }

    [Fact]
    public async Task ACheckThatCouldNotRun_IsTriedAgainNextTime_AndLeavesTheKeySaved()
    {
        _secrets.SetGeminiApiKey(GoodKey);
        _answer = KeyValidation.Timeout;

        await _service.VerifySavedKeyAsync();
        await _service.VerifySavedKeyAsync();

        Assert.Equal(ApiKeyState.Saved, _service.View().KeyState);
        Assert.Equal(2, _validated.Count);
    }

    [Fact]
    public async Task WithNoKey_NothingIsChecked()
    {
        await _service.VerifySavedKeyAsync();

        Assert.Empty(_validated);
    }

    [Theory]
    [InlineData(GoodKey, "AIza ···· 3f9a")]
    [InlineData("short-key", "····")]
    public void TheMask_ShowsFourCharactersEachEnd_AndNothingOfAShortKey(string key, string expected)
    {
        Assert.Equal(expected, TranscriptionSettingsService.Mask(key));
    }

    [Fact]
    public async Task TheViewSentToThePage_NeverContainsTheKey()
    {
        await _service.SaveKeyAsync(GoodKey);

        string json = JsonSerializer.Serialize(_service.View(), BridgeJsonContext.Default.TranscriptionSettingsView);

        Assert.DoesNotContain(GoodKey, json);
        Assert.DoesNotContain("1234567890", json);
    }

    [Fact]
    public async Task TheViewTravelsAsCamelCase()
    {
        await _service.SaveKeyAsync(GoodKey);

        string json = JsonSerializer.Serialize(_service.View(), BridgeJsonContext.Default.TranscriptionSettingsView);

        Assert.Contains("\"mode\":\"auto\"", json);
        Assert.Contains("\"keyState\":\"works\"", json);
        Assert.Contains("\"installedPack\":\"none\"", json);
        Assert.Contains("\"pack\":\"allLanguages\"", json);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
