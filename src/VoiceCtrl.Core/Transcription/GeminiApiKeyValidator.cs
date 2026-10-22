using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Actively checks a Gemini API key against the real API, unlike GeminiTranscriptionClient's
/// PrewarmConnectionAsync (which is deliberately best-effort and swallows every outcome). Used by
/// Setup and Settings, through <see cref="OnlineKeyValidator"/>, to give the user real feedback
/// instead of saving an unverified key.
/// </summary>
public static class GeminiApiKeyValidator
{
    /// <param name="httpClient">Injected for testing. When null, a short-lived HttpClient is
    /// created and disposed for this one call.</param>
    public static async Task<KeyValidation> ValidateAsync(
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
                HttpStatusCode.OK => KeyValidation.Valid,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    or HttpStatusCode.BadRequest or HttpStatusCode.NotFound => KeyValidation.InvalidKey,
                _ => KeyValidation.Unknown,
            };
        }
        catch (HttpRequestException)
        {
            return KeyValidation.NetworkError;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return KeyValidation.Timeout;
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
