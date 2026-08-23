using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;

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
    private readonly bool _compressUpload;
    private readonly PersonalizationStore _personalization;
    private readonly Func<string?> _getForegroundProcessName;

    public GeminiTranscriptionClient(
        AppConfig config,
        Func<string?>? foregroundProcessNameProvider = null,
        PersonalizationStore? personalization = null)
    {
        _personalization = personalization ?? PersonalizationStore.Empty;
        _apiKey = config.GeminiApiKey;
        _modelId = config.GeminiModelId;
        _thinkingLevel = config.ThinkingLevel;
        _cleanupLevel = config.CleanupLevel;
        _styleNotes = config.StyleNotes;
        _enableToneAwareness = config.EnableToneAwareness;
        _promptStyleApps = config.PromptStyleApps;
        _chatStyleApps = config.ChatStyleApps;
        _compressUpload = config.CompressUpload;
        // Overridable so tests/smoke-harnesses can force a specific "foreground app" instead of
        // depending on whatever window actually has focus during the run.
        _getForegroundProcessName = foregroundProcessNameProvider ?? ForegroundAppDetector.GetForegroundProcessName;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _filesApiUploader = new GeminiFilesApiUploader(_apiKey);
    }

    /// <summary>Everything about a single request that depends on which application the speaker is
    /// dictating into. Resolved once per transcription, since a profile can override any of it.</summary>
    private readonly record struct PromptContext(
        string CleanupLevel,
        string? ToneHint,
        string? FormattingHint,
        string? ExtraInstructions);

    /// <summary>
    /// Layers the user's profiles.json entry over the .env lists and then the built-in tables.
    /// A field the profile does not mention falls through rather than blanking, so overriding one
    /// aspect of an app never silently discards the rest.
    /// </summary>
    private PromptContext ResolvePromptContext(string? processName)
    {
        AppProfile? profile = _personalization.Profiles.Resolve(processName);

        string? explicitTone = NullIfBlank(profile?.Tone);
        string? explicitFormatting = NullIfBlank(profile?.Formatting);

        // A tone stated in a profile is the user speaking directly, so it applies even when
        // ENABLE_TONE_AWARENESS is off. That flag governs VoiceCtrl guessing a tone from the
        // foreground app, which is a different thing from the user writing one down.
        string? toneHint = explicitTone switch
        {
            null => _enableToneAwareness ? ToneHintMapper.Resolve(processName) : null,
            _ when IsNone(explicitTone) => null,
            _ => explicitTone,
        };

        string? formattingHint = explicitFormatting switch
        {
            null => FormattingHintMapper.Resolve(processName, _promptStyleApps, _chatStyleApps),
            _ when IsNone(explicitFormatting) => null,
            // An unrecognised value is treated as a typo rather than as an instruction to suppress
            // formatting, so a misspelled profile degrades to the built-in behaviour.
            _ => FormattingHintMapper.ResolveExplicit(explicitFormatting)
                 ?? FormattingHintMapper.Resolve(processName, _promptStyleApps, _chatStyleApps),
        };

        return new PromptContext(
            NullIfBlank(profile?.Cleanup) ?? _cleanupLevel,
            toneHint,
            formattingHint,
            NullIfBlank(profile?.Instructions));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsNone(string value) =>
        string.Equals(value, AppProfile.None, StringComparison.OrdinalIgnoreCase);

    private string BuildPrompt(PromptContext context, IReadOnlyList<string> dictionaryTerms)
    {
        string prompt = string.Format(PromptTemplate, CleanupLevelMapper.Resolve(context.CleanupLevel));

        if (!string.IsNullOrWhiteSpace(_styleNotes))
        {
            prompt += "\n\nAdditional user preferences. Rule 4 (preserve meaning/wording) still " +
                      $"takes priority if these ever conflict with it:\n{_styleNotes.Trim()}";
        }

        if (dictionaryTerms.Count > 0)
        {
            // Framed as spelling rather than as vocabulary on purpose. The failure mode worth
            // guarding against is the model reaching for a listed term because it appears in the
            // prompt, when the speaker said something else entirely.
            prompt += "\n\nSpelling reference. These are names and terms this speaker uses. When " +
                      "something in the audio clearly matches one of these, spell it exactly as " +
                      "written here. Never insert one that was not actually said:\n- " +
                      string.Join("\n- ", dictionaryTerms);
        }

        if (!string.IsNullOrWhiteSpace(context.ToneHint))
        {
            prompt += "\n\nContext hint (best-effort, from the active application, ignore if it " +
                      $"doesn't fit what was actually said): {context.ToneHint}";
        }

        if (!string.IsNullOrWhiteSpace(context.FormattingHint))
        {
            prompt += "\n\nFormatting hint (best-effort, from the active application, ignore if " +
                      $"it doesn't fit what was actually said): {context.FormattingHint}";
        }

        if (!string.IsNullOrWhiteSpace(context.ExtraInstructions))
        {
            prompt += "\n\nInstructions for this specific application, from the user's profile for " +
                      $"it. Rule 4 still takes priority if these conflict with it: {context.ExtraInstructions}";
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

    /// <summary>
    /// Picks what actually goes on the wire. Compression is skipped entirely when disabled, and
    /// falls back to the original WAV whenever the encoder cannot produce something smaller, so
    /// the only way this changes behaviour is by making the upload shorter.
    /// </summary>
    private (byte[] Bytes, string MimeType) PrepareUploadPayload(byte[] wavBytes)
    {
        if (!_compressUpload)
        {
            return (wavBytes, "audio/wav");
        }

        byte[]? mp3Bytes = Mp3Encoder.TryEncode(wavBytes);

        // The size check is not just belt-and-braces: on a very short clip the MP3 container and
        // frame overhead can exceed what compression saves, and sending the larger of the two
        // would defeat the point.
        return mp3Bytes is not null && mp3Bytes.Length < wavBytes.Length
            ? (mp3Bytes, "audio/mpeg")
            : (wavBytes, "audio/wav");
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        (byte[] audioBytes, string mimeType) = PrepareUploadPayload(wavBytes);

        RequestPart audioPart = audioBytes.Length > FilesApiThresholdBytes
            ? new RequestPart
            {
                FileData = new FileData
                {
                    MimeType = mimeType,
                    FileUri = await _filesApiUploader.UploadAsync(audioBytes, mimeType, cancellationToken).ConfigureAwait(true),
                },
            }
            : new RequestPart
            {
                InlineData = new InlineData
                {
                    MimeType = mimeType,
                    Data = Convert.ToBase64String(audioBytes),
                },
            };

        // Sampled once and reused for both hints below, since calling GetForegroundWindow() twice
        // could disagree if the user alt-tabs mid-pipeline. Always sampled: app-aware formatting
        // (structured vs. prose) is a separate feature from tone awareness and must not go dark
        // just because ENABLE_TONE_AWARENESS=false, since only the tone hint is gated by that flag.
        string? processName = _getForegroundProcessName();
        PromptContext promptContext = ResolvePromptContext(processName);
        IReadOnlyList<string> dictionaryTerms = _personalization.DictionaryTerms;

        SimpleFileLogger.LogInfo(
            $"Style detection: process={processName ?? "(none)"} tone={(promptContext.ToneHint is null ? "none" : "set")} " +
            $"formatting={(promptContext.FormattingHint is null ? "none" : "set")} " +
            $"cleanup={promptContext.CleanupLevel} dictionary={dictionaryTerms.Count}");

        var request = new GenerateContentRequest
        {
            Contents =
            [
                new RequestContent
                {
                    Role = "user",
                    Parts = [new RequestPart { Text = BuildPrompt(promptContext, dictionaryTerms) }, audioPart],
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
