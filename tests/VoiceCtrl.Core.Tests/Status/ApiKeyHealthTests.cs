using System.Net;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Status;

public class ApiKeyHealthTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "anything")]
    [InlineData(HttpStatusCode.Forbidden, "Method doesn't allow unregistered callers.")]
    [InlineData(HttpStatusCode.BadRequest, "API key not valid. Please pass a valid API key.")]
    [InlineData(HttpStatusCode.BadRequest, "reason: API_KEY_INVALID")]
    public void IsKeyRejection_TrueForWhatGeminiSaysAboutABadKey(HttpStatusCode status, string message)
    {
        Assert.True(OnlineFailures.IsKeyRejection(new OnlineServiceException(status, message)));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "thinkingLevel not supported")]
    [InlineData(HttpStatusCode.TooManyRequests, "The model is overloaded.")]
    [InlineData(HttpStatusCode.InternalServerError, "API key not valid")]
    public void IsKeyRejection_FalseForOtherErrors(HttpStatusCode status, string message)
    {
        Assert.False(OnlineFailures.IsKeyRejection(new OnlineServiceException(status, message)));
    }

    [Fact]
    public void Reports_RaiseChangedOnlyWhenTheAnswerChanges()
    {
        var health = new ApiKeyHealth();
        var seen = new List<bool>();
        health.Changed += () => seen.Add(health.IsRejected);

        health.ReportAccepted();
        health.ReportRejected();
        health.ReportRejected();
        health.ReportAccepted();

        Assert.Equal([true, false], seen);
    }
}
