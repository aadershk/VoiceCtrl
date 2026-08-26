using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Actively checks a Gemini API key against the real API, unlike GeminiTranscriptionClient's
/// PrewarmConnectionAsync (which is deliberately best-effort and swallows every outcome). Used by
/// first-run setup to give the user real feedback instead of writing an unverified key to .env.
/// </summary>
public static class GeminiApiKeyValidator
{
    public enum Result
    {
        Valid,
        InvalidKey,
        NetworkError,
        Timeout,
        Unknown,
    }

    /// <param name="httpClient">Injected for testing. When null, a short-lived HttpClient is
    /// created and disposed for this one call.</param>
    public static async Task<Result> ValidateAsync(
        string apiKey,
        HttpClient? httpClient = null,
        string modelId = ConfigLoader.DefaultModelId,
        CancellationToken cancellationToken = default)
    {
        HttpClient client = httpClient ?? new HttpClient();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GeminiTranscriptionClient.BaseUrl}/{modelId}");
            request.Headers.Add("x-goog-api-key", apiKey);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using HttpResponseMessage response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => Result.Valid,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    or HttpStatusCode.BadRequest or HttpStatusCode.NotFound => Result.InvalidKey,
                _ => Result.Unknown,
            };
        }
        catch (HttpRequestException)
        {
            return Result.NetworkError;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Timeout;
        }
        finally
        {
            if (httpClient is null)
            {
                client.Dispose();
            }
        }
    }
}
