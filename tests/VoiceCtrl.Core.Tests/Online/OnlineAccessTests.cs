using System.IO;
using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Diagnostics;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Status;
using Xunit;

namespace VoiceCtrl.Core.Tests.Online;

/// <summary>Ticket 60: who Online asks, in what order, and what a failure passes on.</summary>
public sealed class OnlineAccessTests
{
    private const string NvidiaKey = "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";
    private const string GeminiKey = "AIzaSyD4f9Kq2LmN8pQrStUvWxYz0123456789ab";

    private readonly Dictionary<OnlineProvider, string> _keys = [];
    private OnlineProvider _chosen = OnlineProvider.Gemini;
    private OtherProviderSettings _other = new();

    private OnlineAccess Access(ApiKeyHealth? health = null) =>
        new(() => _chosen, p => _keys.GetValueOrDefault(p, ""), () => _other, health ?? new ApiKeyHealth(() => _chosen));

    [Fact]
    public void Chain_IsTheChosenProvider_ThenTheOthersWithAKey_InThePickersOrder()
    {
        _chosen = OnlineProvider.OpenAi;
        _keys[OnlineProvider.OpenAi] = "sk-test-0000000000000000000000";
        _keys[OnlineProvider.Nvidia] = NvidiaKey;
        _keys[OnlineProvider.Gemini] = GeminiKey;

        Assert.Equal([OnlineProvider.OpenAi, OnlineProvider.Gemini, OnlineProvider.Nvidia], Access().Chain());
    }

    [Fact]
    public void Chain_SkipsAProviderWithNoKey_AndOtherWithoutAnAddress()
    {
        _chosen = OnlineProvider.Nvidia;
        _keys[OnlineProvider.Gemini] = GeminiKey;
        _keys[OnlineProvider.Other] = "other-key-000000000000";

        Assert.Equal([OnlineProvider.Gemini], Access().Chain());

        _other = new OtherProviderSettings("https://api.groq.com/openai/v1", "some-model");
        Assert.Equal([OnlineProvider.Gemini, OnlineProvider.Other], Access().Chain());
    }

    [Fact]
    public void Chain_PutsAProviderWhoseKeyWasRefused_Last()
    {
        _keys[OnlineProvider.Gemini] = GeminiKey;
        _keys[OnlineProvider.Nvidia] = NvidiaKey;
        var health = new ApiKeyHealth(() => _chosen);
        health.ReportRejected(OnlineProvider.Gemini);

        Assert.Equal([OnlineProvider.Nvidia, OnlineProvider.Gemini], Access(health).Chain());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RunAsync_ABusyProvider_PassesTheJobToTheNextOneWithAKey(HttpStatusCode status)
    {
        _keys[OnlineProvider.Gemini] = GeminiKey;
        _keys[OnlineProvider.Nvidia] = NvidiaKey;
        List<OnlineProvider> asked = [];

        string answer = await Access().RunAsync("test", provider =>
        {
            asked.Add(provider);
            return provider == OnlineProvider.Gemini
                ? Task.FromException<string>(new OnlineServiceException(status, "busy"))
                : Task.FromResult("from NVIDIA");
        }, CancellationToken.None);

        Assert.Equal("from NVIDIA", answer);
        Assert.Equal([OnlineProvider.Gemini, OnlineProvider.Nvidia], asked);
    }

    [Fact]
    public async Task RunAsync_ARefusedKey_IsReported_AndTheNextProviderAnswers()
    {
        _keys[OnlineProvider.Gemini] = GeminiKey;
        _keys[OnlineProvider.Nvidia] = NvidiaKey;
        var health = new ApiKeyHealth(() => _chosen);

        string answer = await Access(health).RunAsync("test", provider => provider == OnlineProvider.Gemini
            ? Task.FromException<string>(new OnlineServiceException(HttpStatusCode.Forbidden, "API key not valid"))
            : Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", answer);
        Assert.True(health.IsRejected);
        Assert.False(health.IsRejectedFor(OnlineProvider.Nvidia));
    }

    [Fact]
    public async Task RunAsync_ABadRequest_IsNotHiddenByAFallback()
    {
        _keys[OnlineProvider.Gemini] = GeminiKey;
        _keys[OnlineProvider.Nvidia] = NvidiaKey;
        int calls = 0;

        await Assert.ThrowsAsync<OnlineServiceException>(() => Access().RunAsync<string>("test", _ =>
        {
            calls++;
            throw new OnlineServiceException(HttpStatusCode.BadRequest, "thinkingLevel not supported");
        }, CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RunAsync_WhenEveryProviderIsBusy_ThrowsTheLastOnesError()
    {
        _keys[OnlineProvider.Gemini] = GeminiKey;
        _keys[OnlineProvider.Nvidia] = NvidiaKey;

        var ex = await Assert.ThrowsAsync<OnlineServiceException>(() => Access().RunAsync<string>("test", provider =>
            throw new OnlineServiceException(HttpStatusCode.ServiceUnavailable, OnlinePresets.NameOf(provider)), CancellationToken.None));

        Assert.Equal("NVIDIA", ex.Message);
    }

    [Fact]
    public void Migration_AnInstallWithAGeminiKey_StaysOnGemini()
    {
        SettingsStore settings = SettingsStore.InMemory(new UserSettings());
        SecretsStore secrets = SecretsStore.InMemory();
        secrets.SetGeminiApiKey(GeminiKey);

        ProviderMigration.Apply(settings, secrets);

        Assert.Equal(OnlineProvider.Gemini, settings.Current.Transcription.Provider);
    }

    [Fact]
    public void Migration_ANewInstall_GetsTheDefault_AndAChoiceAlreadyMadeIsKept()
    {
        SettingsStore settings = SettingsStore.InMemory(new UserSettings());
        ProviderMigration.Apply(settings, SecretsStore.InMemory());
        Assert.Equal(ProviderMigration.Default, settings.Current.Transcription.Provider);

        SettingsStore chosen = SettingsStore.InMemory(new UserSettings { Transcription = new TranscriptionSection { Provider = OnlineProvider.Nvidia } });
        SecretsStore withGemini = SecretsStore.InMemory();
        withGemini.SetGeminiApiKey(GeminiKey);
        ProviderMigration.Apply(chosen, withGemini);
        Assert.Equal(OnlineProvider.Nvidia, chosen.Current.Transcription.Provider);
    }

    [Fact]
    public void SecretsStore_KeepsOneKeyPerProvider_AcrossARestart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voicectrl-test-secrets-{Guid.NewGuid():N}.bin");
        try
        {
            SecretsStore secrets = SecretsStore.Load(path);
            secrets.SetKey(OnlineProvider.Nvidia, NvidiaKey);
            secrets.SetKey(OnlineProvider.Gemini, GeminiKey);

            SecretsStore reloaded = SecretsStore.Load(path);

            Assert.Equal(NvidiaKey, reloaded.KeyFor(OnlineProvider.Nvidia));
            Assert.Equal(GeminiKey, reloaded.GeminiApiKey);
            Assert.Equal(string.Empty, reloaded.KeyFor(OnlineProvider.OpenAi));
            Assert.DoesNotContain(NvidiaKey, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Diagnostics_NameTheProviderAndModel_NeverAKey()
    {
        var scrubber = new LogScrubber(null, null, [NvidiaKey, GeminiKey]);
        string report = DiagnosticsReport.Build(new DiagnosticsFacts(
            "4.0.0", "Windows", null, new UserSettings { Transcription = new TranscriptionSection { Provider = OnlineProvider.Nvidia } },
            OnlinePresets.For(OnlineProvider.Nvidia, new OtherProviderSettings()), [OnlineProvider.Nvidia, OnlineProvider.Gemini],
            TypeSafeKeySaved: false, PasteAgainTaken: false, DictionaryTerms: 0, Snippets: 0, Profiles: 0, KeptDictations: 0,
            LogLines: [$"INFO Online Dictation (NVIDIA): sent with Authorization: Bearer {NvidiaKey}", $"key {NvidiaKey} in an error"],
            CreatedAt: DateTimeOffset.Now), scrubber);

        Assert.Contains("transcription.provider = nvidia", report);
        Assert.Contains($"Online provider: NVIDIA (Dictation {OnlinePresets.NvidiaAudioModel}", report);
        Assert.Contains("NVIDIA key: saved", report);
        Assert.DoesNotContain(NvidiaKey, report);
        Assert.DoesNotContain(GeminiKey, report);
    }
}
