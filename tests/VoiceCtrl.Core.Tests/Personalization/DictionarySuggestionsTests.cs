using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Personalization;

public sealed class DictionarySuggestionsTests : IDisposable
{
    private const string Key = "ts_live_1234567890abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-suggest-{Guid.NewGuid():N}");
    private readonly SettingsStore _settings = SettingsStore.InMemory(new UserSettings());
    private readonly SecretsStore _secrets = SecretsStore.InMemory();
    private readonly PersonalizationLibrary _library;
    private readonly FakeJudge _judge = new();
    private readonly List<string> _dictations = [];
    private DictionarySuggestions _suggestions;

    public DictionarySuggestionsTests()
    {
        Directory.CreateDirectory(_root);
        _library = new PersonalizationLibrary(Path.Combine(_root, "dictionary.txt"), Path.Combine(_root, "snippets.txt"));
        _suggestions = Create();
    }

    public void Dispose()
    {
        _suggestions.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private DictionarySuggestions Create() =>
        new(_settings, _secrets, _library, () => _dictations, _judge, Path.Combine(_root, "suggestions-ignored.txt"));

    private void TurnOn() => _suggestions.SetOn(true);

    private void SetMode(TranscriptionModePreference mode) =>
        _settings.Update(s => s with { Transcription = s.Transcription with { Mode = mode } });

    /// <summary>Says <paramref name="name"/> mid-sentence in <paramref name="times"/> Dictations.</summary>
    private void Said(string name, int times)
    {
        for (int i = 0; i < times; i++)
        {
            _dictations.Add($"Please send this to {name} today.");
        }
    }

    private async Task<IReadOnlyList<string>> Words() => (await _suggestions.GetAsync()).Items.Select(i => i.Word).ToList();

    [Fact]
    public async Task Off_ByDefault_NothingIsMinedOrSent()
    {
        _secrets.SetTypeSafeApiKey(Key);
        Said("Maya Ortiz", 5);

        SuggestionsView view = await _suggestions.GetAsync();

        Assert.False(view.On);
        Assert.Empty(view.Items);
        Assert.Equal(0, _judge.Calls);
    }

    [Fact]
    public async Task OfflineMode_NeverCallsTypeSafe_AndSuggestsNothing()
    {
        _secrets.SetTypeSafeApiKey(Key);
        TurnOn();
        SetMode(TranscriptionModePreference.Offline);
        Said("Maya Ortiz", 5);

        SuggestionsView view = await _suggestions.GetAsync();

        Assert.True(view.Offline);
        Assert.Empty(view.Items);
        Assert.Equal(0, _judge.Calls);
        Assert.Equal(0, _judge.KeyChecks);
    }

    [Fact]
    public async Task OfflineMode_NeverChecksAKey_Either_ItIsSavedUnchecked()
    {
        SetMode(TranscriptionModePreference.Offline);
        _secrets.SetTypeSafeApiKey(Key);

        await _suggestions.VerifySavedKeyAsync();
        SetupKeyCheck check = await _suggestions.SaveKeyAsync("ts_live_other_key_0000");

        Assert.Equal(SetupKeyCheck.NotChecked, check);
        Assert.Equal("ts_live_other_key_0000", _secrets.TypeSafeApiKey);
        Assert.Equal(0, _judge.KeyChecks);
    }

    [Fact]
    public async Task WithoutAKey_CodeAloneSuggests_WordsSaidInThreeDictationsOrMore()
    {
        TurnOn();
        Said("Maya Ortiz", 3);
        Said("Lisbon Ops", 2);

        IReadOnlyList<DictionarySuggestion> items = (await _suggestions.GetAsync()).Items;

        Assert.Equal([new DictionarySuggestion("Maya Ortiz", 3)], items);
        Assert.Equal(0, _judge.Calls);
    }

    [Fact]
    public async Task WithAKey_TypeSafeRanks_WordsFromTwoDictations_KeepingThoseItJudgesLikely()
    {
        _secrets.SetTypeSafeApiKey(Key);
        TurnOn();
        Said("Maya Ortiz", 2);
        Said("Zoom", 6);
        Said("Lisbon Ops", 4);
        _judge.Nouls["Maya Ortiz"] = 0.95;
        _judge.Nouls["Zoom"] = 0.2;
        _judge.Nouls["Lisbon Ops"] = 0.8;

        IReadOnlyList<DictionarySuggestion> items = (await _suggestions.GetAsync()).Items;

        // Ordered by probability × Dictations: 0.8 × 4 before 0.95 × 2.
        Assert.Equal([new DictionarySuggestion("Lisbon Ops", 4), new DictionarySuggestion("Maya Ortiz", 2)], items);
        Assert.Equal([Key], _judge.KeysUsed.Distinct());
    }

    [Fact]
    public async Task AWordSaidOnce_IsNeverSent()
    {
        _secrets.SetTypeSafeApiKey(Key);
        TurnOn();
        Said("Maya Ortiz", 1);

        await _suggestions.GetAsync();

        Assert.Equal(0, _judge.Calls);
    }

    [Fact]
    public async Task AJudgedWord_IsNotSentAgain()
    {
        _secrets.SetTypeSafeApiKey(Key);
        TurnOn();
        Said("Maya Ortiz", 2);
        _judge.Nouls["Maya Ortiz"] = 0.9;

        await _suggestions.GetAsync();
        await _suggestions.GetAsync();

        Assert.Equal(1, _judge.Calls);
    }

    [Fact]
    public async Task TypeSafeUnreachable_FallsBackToCodeAlone()
    {
        _secrets.SetTypeSafeApiKey(Key);
        TurnOn();
        Said("Maya Ortiz", 3);
        Said("Lisbon Ops", 2);
        _judge.Outcome = TypeSafeOutcome.Unavailable;

        Assert.Equal(["Maya Ortiz"], await Words());
    }

    [Fact]
    public async Task ARefusedKey_ShowsOnTheRow_AndCodeAloneSuggests_WithoutAskingAgain()
    {
        _secrets.SetTypeSafeApiKey(Key);
        TurnOn();
        Said("Maya Ortiz", 3);
        _judge.Outcome = TypeSafeOutcome.KeyRefused;

        Assert.Equal(["Maya Ortiz"], await Words());
        Assert.Equal(ApiKeyState.Rejected, _suggestions.SettingsView().KeyState);

        await _suggestions.GetAsync();
        Assert.Equal(1, _judge.Calls);
    }

    [Fact]
    public async Task AtMostSix_AreOffered()
    {
        TurnOn();
        foreach (string name in new[] { "Ana", "Bram", "Cleo", "Dirk", "Eva", "Femke", "Gijs", "Hanna" })
        {
            Said(name + "berg", 3);
        }

        Assert.Equal(DictionarySuggestions.MaxShown, (await _suggestions.GetAsync()).Items.Count);
    }

    [Fact]
    public async Task Add_PutsTheWordInTheDictionary_AndItIsNoLongerSuggested()
    {
        TurnOn();
        Said("Maya Ortiz", 3);

        Assert.Equal(DictionaryEditError.None, _suggestions.Add("Maya Ortiz"));

        Assert.Equal("Maya Ortiz", _library.Dictionary()[0].Word);
        Assert.Empty(await Words());
    }

    [Fact]
    public async Task Ignore_IsRemembered_AcrossRuns()
    {
        TurnOn();
        Said("Maya Ortiz", 3);
        Said("Lisbon Ops", 3);

        _suggestions.Ignore("Maya Ortiz");
        _suggestions.Dispose();
        _suggestions = Create();

        Assert.Equal(["Lisbon Ops"], await Words());
    }

    [Fact]
    public async Task WordsAlreadyInTheDictionary_AreNotSuggested_NorTheirAlsoHeardAs()
    {
        TurnOn();
        Said("Maya Ortiz", 3);
        Said("Blue Peak", 3);
        _library.SaveWord(null, "Maya Ortiz", "");
        _library.SaveWord(null, "Bluepeak", "Blue Peak");

        Assert.Empty(await Words());
    }

    [Fact]
    public async Task SaveKey_ThatTypeSafeRefuses_LeavesTheSavedKey()
    {
        _secrets.SetTypeSafeApiKey(Key);
        _judge.KeyCheck = TypeSafeOutcome.KeyRefused;

        Assert.Equal(SetupKeyCheck.DidNotWork, await _suggestions.SaveKeyAsync("ts_wrong_key_000000"));
        Assert.Equal(Key, _secrets.TypeSafeApiKey);
    }

    [Fact]
    public async Task SaveKey_ThatWorks_IsSavedAndShownMasked()
    {
        Assert.Equal(SetupKeyCheck.Works, await _suggestions.SaveKeyAsync($"  {Key}  "));

        SuggestionSettingsView view = _suggestions.SettingsView();
        Assert.Equal(Key, _secrets.TypeSafeApiKey);
        Assert.Equal(ApiKeyState.Works, view.KeyState);
        Assert.Equal(TranscriptionSettingsService.Mask(Key), view.KeyMask);
    }

    [Fact]
    public async Task SaveKey_WithNoInternet_IsSavedAnyway()
    {
        _judge.KeyCheck = TypeSafeOutcome.Unavailable;

        Assert.Equal(SetupKeyCheck.NotChecked, await _suggestions.SaveKeyAsync(Key));
        Assert.Equal(ApiKeyState.Saved, _suggestions.SettingsView().KeyState);
    }

    [Fact]
    public async Task ANewKey_IsJudgedAfresh()
    {
        _secrets.SetTypeSafeApiKey(Key);
        TurnOn();
        Said("Maya Ortiz", 2);
        _judge.Nouls["Maya Ortiz"] = 0.9;
        await _suggestions.GetAsync();

        await _suggestions.SaveKeyAsync("ts_live_other_key_0000");
        await _suggestions.GetAsync();

        Assert.Equal(2, _judge.Calls);
    }

    [Fact]
    public void RemoveKey_ClearsIt()
    {
        _secrets.SetTypeSafeApiKey(Key);

        _suggestions.RemoveKey();

        Assert.Equal(ApiKeyState.None, _suggestions.SettingsView().KeyState);
        Assert.Null(_suggestions.SettingsView().KeyMask);
    }

    [Fact]
    public void TurningOnOrOff_IsSaved_AndRaisesChanged()
    {
        int changes = 0;
        _suggestions.Changed += () => changes++;

        TurnOn();

        Assert.True(_settings.Current.Privacy.SuggestDictionaryWords);
        Assert.True(_suggestions.SettingsView().On);
        Assert.True(changes > 0);
    }

    private sealed class FakeJudge : ISuggestionJudge
    {
        public Dictionary<string, double> Nouls { get; } = [];

        public TypeSafeOutcome Outcome { get; set; } = TypeSafeOutcome.Answered;

        public TypeSafeOutcome KeyCheck { get; set; } = TypeSafeOutcome.Answered;

        public int Calls { get; private set; }

        public int KeyChecks { get; private set; }

        public List<string> KeysUsed { get; } = [];

        public Task<TypeSafeOutcome> CheckKeyAsync(string key, CancellationToken cancellationToken)
        {
            KeyChecks++;
            return Task.FromResult(KeyCheck);
        }

        public Task<TypeSafeJudgement> JudgeAsync(string key, IReadOnlyList<SuggestionCandidate> candidates, CancellationToken cancellationToken)
        {
            Calls++;
            KeysUsed.Add(key);
            return Task.FromResult(Outcome == TypeSafeOutcome.Answered
                ? new TypeSafeJudgement(Outcome, candidates.ToDictionary(c => c.Word, c => Nouls.GetValueOrDefault(c.Word, 0.5)))
                : TypeSafeJudgement.Failed(Outcome));
        }
    }
}
