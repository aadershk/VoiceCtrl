using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

public sealed class GeminiTranscriptionClient : ITranscriptionClient
{
    private const string NoSpeechSentinel = "[NO_SPEECH_DETECTED]";
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    // Base64 inflates payload ~33%, and generateContent has a combined-request cap comfortably
    // under the raw 20MB inline_data limit, and this threshold keeps well clear of it. A dictation
    // clip is ~11KB/sec at 16-bit/16kHz mono, so 15MB is well over 20 minutes of continuous speech;
    // this path exists for completeness, not because it's expected to trigger often.
    private const int FilesApiThresholdBytes = 15 * 1024 * 1024;

    private const string PromptTemplate = """
        You are a dictation transcription engine. Listen to the attached audio clip and produce the
        final cleaned-up text the speaker intended to write.

        1. Transcribe accurately first, then silently clean up. Do not show the raw transcript or your work.
        2. {0}
        3. If the speaker corrects or retracts something mid-utterance (e.g. "send it to John...
           actually, no, make that Sarah", "the meeting is at 3, wait, 4 PM"), output only the
           final corrected version. Drop the retracted content entirely. Do not show both.
        4. Preserve the speaker's actual meaning and wording. Do not summarize or add information.
        5. Apply explicit formatting instructions ("new paragraph", "bullet point") instead of
           transcribing them literally. If the speaker enumerates three or more items with clear
           verbal markers ("first... second... third...", "one, two, three") without explicitly
           asking for a list, format them as one anyway.
        6. Transcribe in the same language the speaker used.
        7. If the audio is silent or has no discernible speech, respond with exactly: [NO_SPEECH_DETECTED]
        8. Output ONLY the final text. No preamble, no quotes, no markdown fences, no "Here is...".
        """;

    private readonly HttpClient _httpClient;
    private readonly GeminiFilesApiUploader _filesApiUploader;
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly string _thinkingLevel;
    private readonly string _cleanupLevel;
    private readonly string _styleNotes;
    private readonly bool _enableToneAwareness;
    private readonly IReadOnlyList<string> _promptStyleApps;
    private readonly IReadOnlyList<string> _chatStyleApps;
    private readonly Func<string?> _getForegroundProcessName;

    public GeminiTranscriptionClient(AppConfig config, Func<string?>? foregroundProcessNameProvider = null)
    {
        _apiKey = config.GeminiApiKey;
        _modelId = config.GeminiModelId;
        _thinkingLevel = config.ThinkingLevel;
        _cleanupLevel = config.CleanupLevel;
        _styleNotes = config.StyleNotes;
        _enableToneAwareness = config.EnableToneAwareness;
        _promptStyleApps = config.PromptStyleApps;
        _chatStyleApps = config.ChatStyleApps;
        // Overridable so tests/smoke-harnesses can force a specific "foreground app" instead of
        // depending on whatever window actually has focus during the run.
        _getForegroundProcessName = foregroundProcessNameProvider ?? ForegroundAppDetector.GetForegroundProcessName;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _filesApiUploader = new GeminiFilesApiUploader(_apiKey);
    }

    private string BuildPrompt(string? toneHint, string? formattingHint)
    {
        string prompt = string.Format(PromptTemplate, CleanupLevelMapper.Resolve(_cleanupLevel));

        if (!string.IsNullOrWhiteSpace(_styleNotes))
        {
            prompt += "\n\nAdditional user preferences. Rule 4 (preserve meaning/wording) still " +
                      $"takes priority if these ever conflict with it:\n{_styleNotes.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(toneHint))
        {
            prompt += "\n\nContext hint (best-effort, from the active application, ignore if it " +
                      $"doesn't fit what was actually said): {toneHint}";
        }

        if (!string.IsNullOrWhiteSpace(formattingHint))
        {
            prompt += "\n\nFormatting hint (best-effort, from the active application, ignore if " +
                      $"it doesn't fit what was actually said): {formattingHint}";
        }

        return prompt;
    }

    public async Task PrewarmConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{_modelId}");
            request.Headers.Add("x-goog-api-key", _apiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Best-effort only, and must never surface as an error or block a recording in progress.
            // Broad catch is deliberate: every failure mode here has the identical, silent, no-op
            // outcome, unlike TranscribeAsync's discriminated catches below.
        }
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        RequestPart audioPart = wavBytes.Length > FilesApiThresholdBytes
            ? new RequestPart
            {
                FileData = new FileData
                {
                    MimeType = "audio/wav",
                    FileUri = await _filesApiUploader.UploadAsync(wavBytes, "audio/wav", cancellationToken).ConfigureAwait(true),
                },
            }
            : new RequestPart
            {
                InlineData = new InlineData
                {
                    MimeType = "audio/wav",
                    Data = Convert.ToBase64String(wavBytes),
                },
            };

        // Sampled once and reused for both hints below, since calling GetForegroundWindow() twice
        // could disagree if the user alt-tabs mid-pipeline. Always sampled: app-aware formatting
        // (structured vs. prose) is a separate feature from tone awareness and must not go dark
        // just because ENABLE_TONE_AWARENESS=false, since only the tone hint is gated by that flag.
        string? processName = _getForegroundProcessName();
        string? toneHint = _enableToneAwareness && processName is not null ? ToneHintMapper.Resolve(processName) : null;
        string? formattingHint = processName is not null
            ? FormattingHintMapper.Resolve(processName, _promptStyleApps, _chatStyleApps)
            : null;
        SimpleFileLogger.LogInfo(
            $"Style detection: process={processName ?? "(none)"} tone={(toneHint is null ? "none" : "set")} " +
            $"formatting={(formattingHint is null ? "none" : "set")}");

        var request = new GenerateContentRequest
        {
            Contents =
            [
                new RequestContent
                {
                    Role = "user",
                    Parts = [new RequestPart { Text = BuildPrompt(toneHint, formattingHint) }, audioPart],
                },
            ],
            GenerationConfig = new GenerationConfig { ThinkingConfig = new ThinkingConfig { ThinkingLevel = _thinkingLevel } },
        };

        string url = $"{BaseUrl}/{_modelId}:generateContent";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("x-goog-api-key", _apiKey);
        httpRequest.Content = JsonContent.Create(request);

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(true);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);

        if (!response.IsSuccessStatusCode)
        {
            throw new GeminiApiException(response.StatusCode, ExtractErrorMessage(body));
        }

        GenerateContentResponse? parsed = JsonSerializer.Deserialize<GenerateContentResponse>(body);
        string? text = parsed?.Candidates?
            .FirstOrDefault()?.Content?.Parts?
            .FirstOrDefault(p => p.Text is not null)?.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            string reason = parsed?.PromptFeedback?.BlockReason ?? "empty response";
            throw new GeminiApiException(response.StatusCode, $"Gemini returned no text ({reason}).");
        }

        string trimmed = text.Trim();
        return trimmed == NoSpeechSentinel ? null : trimmed;
    }

    private static string ExtractErrorMessage(string body)
    {
        try
        {
            GeminiErrorEnvelope? envelope = JsonSerializer.Deserialize<GeminiErrorEnvelope>(body);
            return envelope?.Error?.Message ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _filesApiUploader.Dispose();
    }
}
