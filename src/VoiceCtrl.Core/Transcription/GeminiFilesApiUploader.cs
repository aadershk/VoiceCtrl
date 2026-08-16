using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Resumable upload path for clips too large for generateContent's inline_data part. Wire format
/// (start session via response header x-goog-upload-url, then upload+finalize) verified live
/// against ai.google.dev/gemini-api/docs/files. Owns its own
/// HttpClient with a longer timeout than GeminiTranscriptionClient's. This path only runs for
/// large clips, which take proportionally longer to upload.
/// </summary>
internal sealed class GeminiFilesApiUploader : IDisposable
{
    private const string UploadUrl = "https://generativelanguage.googleapis.com/upload/v1beta/files";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiFilesApiUploader(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    public async Task<string> UploadAsync(byte[] fileBytes, string mimeType, CancellationToken cancellationToken)
    {
        string uploadSessionUrl = await StartSessionAsync(fileBytes.Length, mimeType, cancellationToken).ConfigureAwait(true);
        return await UploadBytesAsync(uploadSessionUrl, fileBytes, cancellationToken).ConfigureAwait(true);
    }

    private async Task<string> StartSessionAsync(int numBytes, string mimeType, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl);
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        request.Headers.Add("X-Goog-Upload-Command", "start");
        request.Headers.Add("X-Goog-Upload-Header-Content-Length", numBytes.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);
        request.Content = new StringContent("""{"file": {"display_name": "dictation-clip"}}""", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            throw new GeminiApiException(response.StatusCode, $"Files API upload session failed: {errorBody}");
        }

        if (!response.Headers.TryGetValues("x-goog-upload-url", out IEnumerable<string>? values)
            || values.FirstOrDefault() is not { Length: > 0 } uploadUrl)
        {
            throw new GeminiApiException(response.StatusCode, "Files API did not return an x-goog-upload-url header.");
        }

        return uploadUrl;
    }

    private async Task<string> UploadBytesAsync(string uploadSessionUrl, byte[] fileBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadSessionUrl);
        request.Headers.Add("X-Goog-Upload-Offset", "0");
        request.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        request.Content = new ByteArrayContent(fileBytes);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);

        if (!response.IsSuccessStatusCode)
        {
            throw new GeminiApiException(response.StatusCode, $"Files API byte upload failed: {body}");
        }

        FileUploadResponse? parsed = JsonSerializer.Deserialize<FileUploadResponse>(body);
        string? uri = parsed?.File?.Uri;
        if (string.IsNullOrEmpty(uri))
        {
            throw new GeminiApiException(response.StatusCode, "Files API response did not include a file URI.");
        }

        return uri;
    }

    public void Dispose() => _httpClient.Dispose();
}
