using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Transcription;

public class GeminiApiKeyValidatorTests
{
    [Fact]
    public async Task Ok_ReturnsValid()
    {
        using HttpClient client = CreateClient(HttpStatusCode.OK);

        GeminiApiKeyValidator.Result result = await GeminiApiKeyValidator.ValidateAsync("key", client);

        Assert.Equal(GeminiApiKeyValidator.Result.Valid, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task RejectionStatusCodes_ReturnInvalidKey(HttpStatusCode statusCode)
    {
        using HttpClient client = CreateClient(statusCode);

        GeminiApiKeyValidator.Result result = await GeminiApiKeyValidator.ValidateAsync("key", client);

        Assert.Equal(GeminiApiKeyValidator.Result.InvalidKey, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OtherStatusCodes_ReturnUnknown(HttpStatusCode statusCode)
    {
        using HttpClient client = CreateClient(statusCode);

        GeminiApiKeyValidator.Result result = await GeminiApiKeyValidator.ValidateAsync("key", client);

        Assert.Equal(GeminiApiKeyValidator.Result.Unknown, result);
    }

    [Fact]
    public async Task HttpRequestException_ReturnsNetworkError()
    {
        using HttpClient client = CreateClient(_ => throw new HttpRequestException("no route to host"));

        GeminiApiKeyValidator.Result result = await GeminiApiKeyValidator.ValidateAsync("key", client);

        Assert.Equal(GeminiApiKeyValidator.Result.NetworkError, result);
    }

    [Fact]
    public async Task RequestTimesOut_ReturnsTimeout()
    {
        using HttpClient client = CreateClient(_ => throw new TaskCanceledException("the operation timed out"));

        GeminiApiKeyValidator.Result result = await GeminiApiKeyValidator.ValidateAsync("key", client);

        Assert.Equal(GeminiApiKeyValidator.Result.Timeout, result);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesInsteadOfReturningTimeout()
    {
        using HttpClient client = CreateClient(_ => throw new TaskCanceledException("cancelled"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => GeminiApiKeyValidator.ValidateAsync("key", client, cancellationToken: cts.Token));
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode) =>
        CreateClient(_ => new HttpResponseMessage(statusCode));

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new FakeHandler(responder));

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
