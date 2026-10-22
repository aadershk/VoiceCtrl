using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.EmailRequests;

/// <summary>Online: one text-only Gemini call for the body. The request's words already came from
/// the Dictation's own Gemini call, so no audio is sent again.</summary>
internal sealed class GeminiEmailBodyWriter : IEmailBodyWriter, IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<string> _apiKey;
    private readonly string _modelId;
    private readonly string _thinkingLevel;

    /// <param name="apiKey">Read at each request, so a key saved while the app runs is used at once.</param>
    /// <param name="handler">Test seam for the network; null uses the real one.</param>
    public GeminiEmailBodyWriter(AppConfig config, Func<string> apiKey, HttpMessageHandler? handler = null)
    {
        _apiKey = apiKey;
        _modelId = config.GeminiModelId;
        _thinkingLevel = config.ThinkingLevel;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public bool IsReady => AppConfig.IsUsableApiKey(_apiKey());
    public Task<string?> WriteBodyAsync(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        var body = new GenerateContentRequest
        {
            Contents =
            [
                new RequestContent
                {
                    Role = "user",
                    Parts = [new RequestPart { Text = EmailText.GeminiPrompt(dictionary, request, request.Style) }],
                },
            ],
            GenerationConfig = new GenerationConfig { ThinkingConfig = new ThinkingConfig { ThinkingLevel = _thinkingLevel } },
        };

        // The primary model, then the backup if it is busy or slow to answer (ticket 60).
        return ModelAttempts.RunAsync(
            GeminiTranscriptionClient.ModelsFor(_modelId), ModelAttempts.TextDeadline(600),
            (model, token) => AskAsync(model, body, token), "Online email (Gemini)", cancellationToken);
    }

    private async Task<string?> AskAsync(string model, GenerateContentRequest body, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{GeminiTranscriptionClient.BaseUrl}/{model}:generateContent");
        httpRequest.Headers.Add("x-goog-api-key", _apiKey());
        httpRequest.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new OnlineServiceException(response.StatusCode, ErrorMessage(json), OnlineProvider.Gemini);
        }

        GenerateContentResponse? parsed = JsonSerializer.Deserialize<GenerateContentResponse>(json);
        return parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault(p => p.Text is not null)?.Text;
    }
    private static string ErrorMessage(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GeminiErrorEnvelope>(json)?.Error?.Message ?? json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    public void Dispose() => _http.Dispose();
}
