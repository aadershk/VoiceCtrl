using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Rewrite;

public class AdaptiveRewriteClientTests
{
    private sealed class FakeRewriter(string name) : IRewriteClient
    {
        public int Calls { get; private set; }

        public Exception? Throws { get; set; }

        public Task<RewriteOutcome> RewriteAsync(byte[] instructionAudio, string source, CancellationToken cancellationToken, string? heard = null)
        {
            Calls++;
            return Throws is null ? Task.FromResult(RewriteOutcome.Rewritten(name)) : Task.FromException<RewriteOutcome>(Throws);
        }

        public Task<string?> HearAsync(byte[] instructionAudio, CancellationToken cancellationToken)
        {
            Calls++;
            return Throws is null ? Task.FromResult<string?>(name) : Task.FromException<string?>(Throws);
        }
    }

    private static (AdaptiveRewriteClient Client, FakeRewriter Online, FakeRewriter Offline, ApiKeyHealth Health) Build(
        TranscriptionModePreference mode, bool keyConfigured = true, bool network = true)
    {
        var online = new FakeRewriter("online");
        var offline = new FakeRewriter("offline");
        var health = new ApiKeyHealth();
        var client = new AdaptiveRewriteClient(() => mode, online, offline, () => keyConfigured, health, () => network);
        return (client, online, offline, health);
    }

    private static Task<RewriteOutcome> Run(AdaptiveRewriteClient client) => client.RewriteAsync([1], "text", CancellationToken.None);

    [Theory]
    [InlineData(TranscriptionModePreference.Offline, "offline")]
    [InlineData(TranscriptionModePreference.Online, "online")]
    public async Task Hearing_FollowsTheModeLikeARewrite(TranscriptionModePreference mode, string heardBy)
    {
        (AdaptiveRewriteClient client, _, _, _) = Build(mode);

        Assert.Equal(heardBy, await client.HearAsync([1], CancellationToken.None));
    }

    [Fact]
    public async Task Hearing_InAuto_FallsBackToTheLocalModel_WhenGeminiIsBusy()
    {
        (AdaptiveRewriteClient client, FakeRewriter online, _, _) = Build(TranscriptionModePreference.Auto);
        online.Throws = new OnlineServiceException(System.Net.HttpStatusCode.ServiceUnavailable, "busy");

        Assert.Equal("offline", await client.HearAsync([1], CancellationToken.None));
    }

    [Fact]
    public async Task Offline_UsesTheLocalModelOnly()
    {
        var (client, online, offline, _) = Build(TranscriptionModePreference.Offline);

        RewriteOutcome outcome = await Run(client);

        Assert.Equal("offline", outcome.Text);
        Assert.Equal(0, online.Calls);
    }

    [Fact]
    public async Task Online_UsesGeminiAndMarksTheKeyAsWorking()
    {
        var (client, online, offline, health) = Build(TranscriptionModePreference.Online);
        health.ReportRejected();

        RewriteOutcome outcome = await Run(client);

        Assert.Equal("online", outcome.Text);
        Assert.Equal(0, offline.Calls);
        Assert.False(health.IsRejected);
    }

    [Fact]
    public async Task Online_AnApiError_PropagatesInsteadOfFallingBack()
    {
        var (client, online, offline, _) = Build(TranscriptionModePreference.Online);
        online.Throws = new OnlineServiceException(HttpStatusCode.ServiceUnavailable, "busy");

        await Assert.ThrowsAsync<OnlineServiceException>(() => Run(client));
        Assert.Equal(0, offline.Calls);
    }

    [Fact]
    public async Task Online_ARefusedKey_IsReportedAndStillThrows()
    {
        var (client, online, _, health) = Build(TranscriptionModePreference.Online);
        online.Throws = new OnlineServiceException(HttpStatusCode.Forbidden, "API key not valid");

        await Assert.ThrowsAsync<OnlineServiceException>(() => Run(client));
        Assert.True(health.IsRejected);
    }

    [Fact]
    public async Task Auto_WithAWorkingKeyAndNetwork_GoesOnline()
    {
        var (client, online, offline, _) = Build(TranscriptionModePreference.Auto);

        Assert.Equal("online", (await Run(client)).Text);
        Assert.Equal(0, offline.Calls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Auto_WithoutAKeyOrANetwork_GoesOffline(bool keyConfigured, bool network)
    {
        var (client, online, _, _) = Build(TranscriptionModePreference.Auto, keyConfigured, network);

        Assert.Equal("offline", (await Run(client)).Text);
        Assert.Equal(0, online.Calls);
    }

    [Fact]
    public async Task Auto_AKeyGeminiRefused_GoesOffline()
    {
        var (client, online, _, health) = Build(TranscriptionModePreference.Auto);
        health.ReportRejected();

        Assert.Equal("offline", (await Run(client)).Text);
        Assert.Equal(0, online.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Auto_ATransientErrorOrRefusedKey_FallsBackToTheLocalModel(HttpStatusCode status)
    {
        var (client, online, offline, _) = Build(TranscriptionModePreference.Auto);
        online.Throws = new OnlineServiceException(status, status == HttpStatusCode.Forbidden ? "API key not valid" : "busy");

        Assert.Equal("offline", (await Run(client)).Text);
        Assert.Equal(1, offline.Calls);
    }

    [Fact]
    public async Task Auto_ANetworkFailure_FallsBackToTheLocalModel()
    {
        var (client, online, _, _) = Build(TranscriptionModePreference.Auto);
        online.Throws = new HttpRequestException("no route");

        Assert.Equal("offline", (await Run(client)).Text);
    }

    [Fact]
    public async Task Auto_ARealRequestError_IsNotHiddenByTheFallback()
    {
        var (client, online, offline, _) = Build(TranscriptionModePreference.Auto);
        online.Throws = new OnlineServiceException(HttpStatusCode.BadRequest, "bad request");

        await Assert.ThrowsAsync<OnlineServiceException>(() => Run(client));
        Assert.Equal(0, offline.Calls);
    }
}
