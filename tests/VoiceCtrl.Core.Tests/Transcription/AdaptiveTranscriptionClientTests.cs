using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class AdaptiveTranscriptionClientTests
{
    [Fact]
    public async Task OfflinePreference_AlwaysUsesLocalClient_EvenWithKeyConfigured()
    {
        var online = new FakeTranscriptionClient(result: "online");
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Offline, online, offline, isApiKeyConfigured: true);

        string? result = await client.TranscribeAsync([]);

        Assert.Equal("offline", result);
        Assert.Equal(0, online.CallCount);
        Assert.Equal(1, offline.CallCount);
    }

    [Fact]
    public async Task OnlinePreference_AlwaysUsesOnlineClient_NoFallbackOnFailure()
    {
        var online = new FakeTranscriptionClient(exception: new GeminiApiException(HttpStatusCode.BadRequest, "bad request"));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Online, online, offline, isApiKeyConfigured: true);

        await Assert.ThrowsAsync<GeminiApiException>(() => client.TranscribeAsync([]));

        Assert.Equal(0, offline.CallCount);
    }

    [Fact]
    public async Task Auto_NoApiKeyConfigured_UsesLocalClientSilently_NoFallbackEventRaised()
    {
        var online = new FakeTranscriptionClient(result: "online");
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: false);
        bool fellBack = false;
        client.FellBackToOffline += () => fellBack = true;

        string? result = await client.TranscribeAsync([]);

        Assert.Equal("offline", result);
        Assert.Equal(0, online.CallCount);
        Assert.False(fellBack);
    }

    [Fact]
    public async Task Auto_ConnectivityException_FallsBackToLocalAndRaisesEvent()
    {
        var online = new FakeTranscriptionClient(exception: new HttpRequestException("no route to host"));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);
        bool fellBack = false;
        client.FellBackToOffline += () => fellBack = true;

        string? result = await client.TranscribeAsync([]);

        Assert.Equal("offline", result);
        Assert.Equal(1, online.CallCount);
        Assert.Equal(1, offline.CallCount);
        Assert.True(fellBack);
    }

    [Fact]
    public async Task Auto_TimeoutException_FallsBackToLocalAndRaisesEvent()
    {
        var online = new FakeTranscriptionClient(exception: new TaskCanceledException("timed out"));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);

        string? result = await client.TranscribeAsync([]);

        Assert.Equal("offline", result);
    }

    [Fact]
    public async Task Auto_ApiLevelException_PropagatesUnchanged_DoesNotFallBack()
    {
        // The deliberate scoping: a real API-level error must stay visible, not get masked as
        // "fell back to offline, still worked."
        var online = new FakeTranscriptionClient(exception: new GeminiApiException(HttpStatusCode.BadRequest, "thinkingLevel not supported"));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);
        bool fellBack = false;
        client.FellBackToOffline += () => fellBack = true;

        await Assert.ThrowsAsync<GeminiApiException>(() => client.TranscribeAsync([]));

        Assert.Equal(0, offline.CallCount);
        Assert.False(fellBack);
    }

    [Fact]
    public async Task Auto_TransientApiException_ServiceUnavailable_FallsBackToLocalAndRaisesEvent()
    {
        // 503 "high demand" is the exact shape of error seen in production: transient, worth a
        // fallback instead of failing the whole dictation outright.
        var online = new FakeTranscriptionClient(exception: new GeminiApiException(
            HttpStatusCode.ServiceUnavailable, "This model is currently experiencing high demand. Please try again shortly."));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);
        bool fellBack = false;
        client.FellBackToOffline += () => fellBack = true;

        string? result = await client.TranscribeAsync([]);

        Assert.Equal("offline", result);
        Assert.Equal(1, online.CallCount);
        Assert.Equal(1, offline.CallCount);
        Assert.True(fellBack);
    }

    [Fact]
    public async Task Auto_TransientApiException_TooManyRequests_FallsBackToLocalAndRaisesEvent()
    {
        var online = new FakeTranscriptionClient(exception: new GeminiApiException(HttpStatusCode.TooManyRequests, "rate limited"));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);

        string? result = await client.TranscribeAsync([]);

        Assert.Equal("offline", result);
    }

    [Fact]
    public async Task Auto_TransientApiException_BadGatewayAndGatewayTimeout_FallBackToLocal()
    {
        var offlineForBadGateway = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient badGatewayClient = CreateClient(
            TranscriptionModePreference.Auto,
            new FakeTranscriptionClient(exception: new GeminiApiException(HttpStatusCode.BadGateway, "bad gateway")),
            offlineForBadGateway,
            isApiKeyConfigured: true);
        Assert.Equal("offline", await badGatewayClient.TranscribeAsync([]));

        var offlineForGatewayTimeout = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient gatewayTimeoutClient = CreateClient(
            TranscriptionModePreference.Auto,
            new FakeTranscriptionClient(exception: new GeminiApiException(HttpStatusCode.GatewayTimeout, "gateway timeout")),
            offlineForGatewayTimeout,
            isApiKeyConfigured: true);
        Assert.Equal("offline", await gatewayTimeoutClient.TranscribeAsync([]));
    }

    [Fact]
    public async Task Auto_CallerCancellation_TransientApiException_PropagatesInsteadOfFallingBack()
    {
        var online = new FakeTranscriptionClient(exception: new GeminiApiException(HttpStatusCode.ServiceUnavailable, "high demand"));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<GeminiApiException>(() => client.TranscribeAsync([], cts.Token));

        Assert.Equal(0, offline.CallCount);
    }

    [Fact]
    public async Task Auto_CallerCancellation_PropagatesInsteadOfFallingBack()
    {
        var online = new FakeTranscriptionClient(exception: new TaskCanceledException("timed out"));
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.TranscribeAsync([], cts.Token));

        Assert.Equal(0, offline.CallCount);
    }

    [Fact]
    public async Task Auto_PrewarmConnection_WarmsBothClients()
    {
        var online = new FakeTranscriptionClient(result: "online");
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: true);

        await client.PrewarmConnectionAsync();

        Assert.Equal(1, online.PrewarmCount);
        Assert.Equal(1, offline.PrewarmCount);
    }

    [Fact]
    public async Task Auto_PrewarmConnection_NoApiKey_OnlyWarmsLocal()
    {
        var online = new FakeTranscriptionClient(result: "online");
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Auto, online, offline, isApiKeyConfigured: false);

        await client.PrewarmConnectionAsync();

        Assert.Equal(0, online.PrewarmCount);
        Assert.Equal(1, offline.PrewarmCount);
    }

    [Fact]
    public async Task OfflinePreference_PrewarmConnection_OnlyWarmsLocal()
    {
        var online = new FakeTranscriptionClient(result: "online");
        var offline = new FakeTranscriptionClient(result: "offline");
        AdaptiveTranscriptionClient client = CreateClient(TranscriptionModePreference.Offline, online, offline, isApiKeyConfigured: true);

        await client.PrewarmConnectionAsync();

        Assert.Equal(0, online.PrewarmCount);
        Assert.Equal(1, offline.PrewarmCount);
    }

    private static AdaptiveTranscriptionClient CreateClient(
        TranscriptionModePreference mode,
        ITranscriptionClient online,
        ITranscriptionClient offline,
        bool isApiKeyConfigured) =>
        new(TranscriptionModeStore.ForTesting(mode), online, offline, isApiKeyConfigured);

    private sealed class FakeTranscriptionClient(string? result = null, Exception? exception = null) : ITranscriptionClient
    {
        public int CallCount { get; private set; }
        public int PrewarmCount { get; private set; }

        public Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return exception is not null ? Task.FromException<string?>(exception) : Task.FromResult(result);
        }

        public Task PrewarmConnectionAsync(CancellationToken cancellationToken = default)
        {
            PrewarmCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
