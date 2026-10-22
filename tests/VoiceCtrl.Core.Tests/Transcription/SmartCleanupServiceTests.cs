using System.IO;
using System.IO.Compression;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public sealed class SmartCleanupServiceTests : IDisposable
{
    private static readonly PromptContext Standard = new("standard", null, null, null, false);
    private const string Retraction = "Send it to John, actually, make that Sarah, by Friday.";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-smart-{Guid.NewGuid():N}");
    private readonly SettingsStore _settings;
    private readonly List<FakeModel> _created = [];
    private Func<ModelRequest, CancellationToken, Task<string?>> _reply = (_, _) => Task.FromResult<string?>("Send it to Sarah by Friday.");

    public SmartCleanupServiceTests()
    {
        _settings = SettingsStore.InMemory(new UserSettings { Transcription = new TranscriptionSection { SmartCleanupTier = SmartCleanupTier.Standard } });
    }

    private sealed class FakeModel(Func<ModelRequest, CancellationToken, Task<string?>> reply) : ISmartCleanupModel
    {
        public bool Disposed { get; private set; }

        public List<ModelRequest> Requests { get; } = [];

        public Task<string?> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return reply(request, cancellationToken);
        }

        public void Dispose() => Disposed = true;
    }

    private SmartCleanupService NewService(bool installed = true)
    {
        if (installed)
        {
            InstallPack(SmartCleanupPacks.Standard);
        }

        var packs = new SmartCleanupPackManager(_settings, _root, (_, _, _) => Task.CompletedTask, () => Task.CompletedTask, () => TimeSpan.Zero);
        return new SmartCleanupService(_settings, packs, 4, (_, _) =>
        {
            var model = new FakeModel((request, token) => _reply(request, token));
            _created.Add(model);
            return model;
        });
    }

    private void InstallPack(OfflineModel model)
    {
        string folder = model.PathUnder(_root);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, SmartCleanupPacks.ModelFile), [1]);
        using (ZipArchive zip = ZipFile.Open(Path.Combine(folder, SmartCleanupPacks.ServerArchive), ZipArchiveMode.Create))
        {
            zip.CreateEntry("llama-server.exe");
        }
    }

    private Task<SmartCleanupResult> Clean(SmartCleanupService service) =>
        service.Clean(Retraction, Standard, "", [], CancellationToken.None);

    [Fact]
    public void NothingInstalled_IsNotAvailable() => Assert.False(NewService(installed: false).IsAvailable);

    [Fact]
    public void Installed_IsAvailable_UntilTheSwitchIsOff()
    {
        SmartCleanupService service = NewService();
        Assert.True(service.IsAvailable);

        _settings.Update(s => s with { Transcription = s.Transcription with { SmartCleanup = false } });

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task Clean_UsesTheInstalledModel_AndReusesItForTheNextDictation()
    {
        SmartCleanupService service = NewService();

        SmartCleanupResult first = await Clean(service);
        await Clean(service);

        Assert.True(first.Applied);
        Assert.Equal("Send it to Sarah by Friday.", first.Text);
        Assert.Single(_created);
        Assert.Equal(2, _created[0].Requests.Count);
    }

    [Fact]
    public async Task Clean_WhenNotInstalled_KeepsTheRuleCleanedText()
    {
        SmartCleanupService service = NewService(installed: false);

        SmartCleanupResult result = await Clean(service);

        Assert.False(result.Applied);
        Assert.Equal(Retraction, result.Text);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task ThreeFailedDictationsInARow_EndSmartCleanupForTheSession()
    {
        _reply = (_, _) => throw new InvalidOperationException("server died");
        SmartCleanupService service = NewService();

        for (int i = 0; i < 3; i++)
        {
            await Clean(service);
        }

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task AGoodDictation_ResetsTheFailureCount()
    {
        SmartCleanupService service = NewService();
        _reply = (_, _) => throw new InvalidOperationException("server died");
        await Clean(service);
        await Clean(service);
        _reply = (_, _) => Task.FromResult<string?>("Send it to Sarah by Friday.");
        await Clean(service);
        _reply = (_, _) => throw new InvalidOperationException("server died");
        await Clean(service);
        await Clean(service);

        Assert.True(service.IsAvailable);
    }

    /// <summary>Ticket 55: a Dictation into a new kind of app ends while the model is still reading
    /// that app's notes. Sent alongside, the cleanup would land in the server's other slot and read
    /// the whole prompt cold; it waits for the reading instead, and the wait is not the model being slow.</summary>
    [Fact]
    public async Task ACleanupDuringThePrime_WaitsForIt_AndTheWaitDoesNotCountAsSlow()
    {
        TaskCompletionSource primeRead = new();
        List<string> order = [];
        _reply = async (request, _) =>
        {
            if (request.MaxTokens == 1)
            {
                order.Add("prime");
                await primeRead.Task;
                return "x";
            }

            order.Add("clean");
            return "Send it to Sarah by Friday.";
        };
        SmartCleanupService service = NewService();

        Task priming = service.PrimeAsync(Standard, "", [], CancellationToken.None);
        Task<SmartCleanupResult> cleaning = Clean(service);
        await Task.Delay(TimeSpan.FromSeconds(6)); // longer than the Standard tier's whole wait for this text
        Assert.DoesNotContain("clean", order);

        primeRead.SetResult();
        await priming;
        SmartCleanupResult result = await cleaning;

        Assert.True(result.Applied);
        Assert.Equal("clean", order[^1]);
    }

    [Fact]
    public async Task Unload_EndsTheModel_AndTheNextDictationStartsANewOne()
    {
        SmartCleanupService service = NewService();
        await Clean(service);

        service.Unload();
        await Clean(service);

        Assert.True(_created[0].Disposed);
        Assert.Equal(2, _created.Count);
    }

    [Fact]
    public async Task Prime_SendsTheRulesWithRoomForOneToken()
    {
        SmartCleanupService service = NewService();

        await service.PrimeAsync(Standard, "", [], CancellationToken.None);

        // The cleanup rules first, then the word-mending rules, each read once and kept by the server.
        Assert.Equal(2, _created[0].Requests.Count);
        ModelRequest request = _created[0].Requests[0];
        Assert.Equal(1, request.MaxTokens);
        Assert.Equal(SmartCleanupPrompt.System(Standard.CleanupLevel, ""), request.System);
    }

    [Fact]
    public async Task Prime_KeepsEachRuleSetInASlotOfItsOwn_SoTheyCanBeSavedToDisk()
    {
        SmartCleanupService service = NewService();

        await service.PrimeAsync(Standard, "", [], CancellationToken.None);

        // Slot 0 holds the cleanup rules and slot 1 the mending rules; only a prime is kept, since a
        // Dictation's own words must never be written to disk.
        Assert.Equal([0, 1], _created[0].Requests.Select(r => r.KeepInSlot));
    }

    [Fact]
    public async Task Clean_NeverAsksToKeepItsSlot()
    {
        SmartCleanupService service = NewService();

        await Clean(service);

        Assert.All(_created[0].Requests, r => Assert.Null(r.KeepInSlot));
    }

    [Fact]
    public async Task Prime_ForWordForWord_SkipsTheMendingRules()
    {
        SmartCleanupService service = NewService();

        await service.PrimeAsync(new PromptContext(CleanupLevels.Verbatim, null, null, null, false), "", [], CancellationToken.None);

        Assert.Single(_created[0].Requests);
    }

    [Fact]
    public async Task Prime_WhenOff_DoesNotStartTheModel()
    {
        SmartCleanupService service = NewService();
        _settings.Update(s => s with { Transcription = s.Transcription with { SmartCleanup = false } });

        await service.PrimeAsync(Standard, "", [], CancellationToken.None);

        Assert.Empty(_created);
    }

    private static readonly BuiltInVocabulary MendVocabulary = new(
        ["Eindhoven"], ["we", "flew", "to", "and", "then", "took", "the", "train", "for", "a", "weekend"]);

    [Fact]
    public async Task Mend_FillsTheTrace_WithThePairsAndTheModelTime()
    {
        _reply = (_, _) => Task.FromResult<string?>("eindovind -> eindhoven\nweekend -> fortnight");
        SmartCleanupService service = NewService();
        var trace = new EngineTrace();

        string mended = await service.Mend("We flew to Eindovind and then took the train.", Standard, [], MendVocabulary, CancellationToken.None, trace);

        Assert.Equal("We flew to Eindhoven and then took the train.", mended);
        Assert.Equal("ran", trace.Mend);
        Assert.Equal(2, trace.MendProposed);
        Assert.Equal(1, trace.MendRefused);
        Assert.Equal([new WordPair("eindovind", "Eindhoven")], trace.MendApplied);
    }

    [Fact]
    public async Task Mend_WithNothingToCheck_LeavesTheTraceSkipped()
    {
        SmartCleanupService service = NewService();
        var trace = new EngineTrace();

        await service.Mend("We flew to the train for a weekend.", Standard, [], MendVocabulary, CancellationToken.None, trace);

        Assert.Equal("skipped", trace.Mend);
        Assert.Empty(_created.SelectMany(model => model.Requests));
    }

    [Fact]
    public async Task Mend_WhenTheModelIsTooSlow_RecordsATimeout()
    {
        _reply = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        };
        SmartCleanupService service = NewService();
        var trace = new EngineTrace();

        await service.Mend("We flew to Eindovind and then took the train.", Standard, [], MendVocabulary, CancellationToken.None, trace);

        Assert.Equal("timed out", trace.Mend);
    }

    [Fact]
    public async Task Clean_FillsTheTrace_WithItsOutcome()
    {
        SmartCleanupService service = NewService();
        var trace = new EngineTrace();

        await service.Clean(Retraction, Standard, "", [], CancellationToken.None, trace);

        Assert.Equal("applied", trace.SmartCleanup);
    }

    [Fact]
    public void TheBetterModel_IsGivenLongerToAnswer()
    {
        Assert.True(
            SmartCleanupService.TimeoutFor(SmartCleanupTier.Better, 28) > SmartCleanupService.TimeoutFor(SmartCleanupTier.Standard, 28));
        Assert.True(SmartCleanupService.TimeoutFor(SmartCleanupTier.Better, 500) <= TimeSpan.FromSeconds(20));
        // Ticket 55, measured: a 37-word message took about 8 s with the prompt cached.
        Assert.True(SmartCleanupService.TimeoutFor(SmartCleanupTier.Better, 37) >= TimeSpan.FromSeconds(11));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
