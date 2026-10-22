using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

public sealed class GeminiTranscriptionClient : ITranscriptionClient
{
    private const string NoSpeechSentinel = "[NO_SPEECH_DETECTED]";
    internal const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    // Base64 inflates payload ~33%, and generateContent has a combined-request cap comfortably
    // under the raw 20MB inline_data limit, and this threshold keeps well clear of it. A dictation
    // clip is ~11KB/sec at 16-bit/16kHz mono, so 15MB is well over 20 minutes of continuous speech;
    // this path exists for completeness, not because it's expected to trigger often.
    private const int FilesApiThresholdBytes = 15 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly GeminiFilesApiUploader _filesApiUploader;
    private readonly Func<string> _apiKey;
    private readonly string _modelId;
    private IReadOnlyList<string> _models;

    /// <summary>The models to try, primary first; the benches set one to measure it alone.</summary>
    internal IReadOnlyList<string> Models { get => _models; init => _models = value; }
    private readonly string _thinkingLevel;
    private readonly Func<string> _cleanupLevel;
    private readonly Func<bool> _pressEnter;
    private readonly Func<StyleSettings> _styles;
    private readonly string _styleNotes;
    private readonly bool _enableToneAwareness;
    private readonly IReadOnlyList<string> _promptStyleApps;
    private readonly IReadOnlyList<string> _chatStyleApps;
    private readonly bool _compressUpload;
    private readonly PersonalizationStore _personalization;
    private readonly Func<string?> _getForegroundProcessName;
    private readonly Func<string?> _getForegroundWindowTitle;
    private readonly SmartCleanupLedger? _ledger;

    /// <param name="apiKey">Reads the key at each request, so a key saved while the app runs
    /// (Setup, Settings) is used at once. Defaults to the key in <paramref name="config"/>.</param>
    /// <param name="cleanupLevel">Reads the default Cleanup Level at each request, so a change in
    /// Settings applies to the next Dictation. Defaults to the level in <paramref name="config"/>.</param>
    /// <param name="pressEnter">The Press Enter setting, read at each request. Defaults to off.</param>
    /// <param name="styles">The Style screen's settings, read at each request. Defaults to a fresh install's.</param>
    /// <param name="ledger">Told the transcript Gemini edited, for the Dictations screen's Original.</param>
    /// <param name="foregroundWindowTitleProvider">The foreground window's title, which names a browser's
    /// site. Defaults to the real one, or to none when <paramref name="foregroundProcessNameProvider"/> is given.</param>
    public GeminiTranscriptionClient(
        AppConfig config,
        Func<string?>? foregroundProcessNameProvider = null,
        PersonalizationStore? personalization = null,
        Func<string>? apiKey = null,
        Func<string>? cleanupLevel = null,
        Func<bool>? pressEnter = null,
        SmartCleanupLedger? ledger = null,
        Func<string?>? foregroundWindowTitleProvider = null,
        Func<StyleSettings>? styles = null)
    {
        _ledger = ledger;
        _personalization = personalization ?? PersonalizationStore.Empty;
        _apiKey = apiKey ?? (() => config.GeminiApiKey);
        _modelId = config.GeminiModelId;
        _models = ModelsFor(_modelId);
        _thinkingLevel = config.ThinkingLevel;
        _cleanupLevel = cleanupLevel ?? (() => config.CleanupLevel);
        _pressEnter = pressEnter ?? (() => false);
        _styles = styles ?? (() => new StyleSettings());
        _styleNotes = config.StyleNotes;
        _enableToneAwareness = config.EnableToneAwareness;
        _promptStyleApps = config.PromptStyleApps;
        _chatStyleApps = config.ChatStyleApps;
        _compressUpload = config.CompressUpload;
        // Overridable so tests/smoke-harnesses can force a specific "foreground app" instead of
        // depending on whatever window actually has focus during the run.
        _getForegroundProcessName = foregroundProcessNameProvider ?? ForegroundAppDetector.GetForegroundProcessName;
        _getForegroundWindowTitle = foregroundWindowTitleProvider ??
                                    (foregroundProcessNameProvider is null ? ForegroundAppDetector.GetForegroundWindowTitle : () => null);
        // The request deadline is set per request (RequestTimeoutFor), because a 20-minute recording
        // needs minutes to answer where a sentence needs seconds.
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _filesApiUploader = new GeminiFilesApiUploader(_apiKey);
    }

    public async Task PrewarmConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{_modelId}");
            request.Headers.Add("x-goog-api-key", _apiKey());
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

    /// <summary>
    /// 30 s for a short clip, as before, plus half a second per second of audio, so the longest
    /// recording (20 minutes) gets about ten minutes to be answered instead of failing at 30 s.
    /// </summary>
    internal static TimeSpan RequestTimeoutFor(int wavByteCount)
    {
        const double bytesPerSecond = 32_000; // 16 kHz, 16-bit, mono
        return TimeSpan.FromSeconds(30 + wavByteCount / bytesPerSecond * 0.5);
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
        PromptContext promptContext = GeminiPrompt.ContextFor(
            _personalization.Profiles.Resolve(processName), processName,
            new PromptDefaults(_cleanupLevel(), _enableToneAwareness, _promptStyleApps, _chatStyleApps, _pressEnter(), _styles()),
            _getForegroundWindowTitle());
        IReadOnlyList<DictionaryEntry> dictionary = _personalization.Dictionary;

        // The kind, never the window title: a title can hold an email's subject.
        SimpleFileLogger.LogInfo(
            $"Style detection: process={processName ?? "(none)"} kind={promptContext.Kind} tone={(promptContext.ToneHint is null ? "none" : "set")} " +
            $"formatting={(promptContext.FormattingHint is null ? "none" : "set")} " +
            $"cleanup={promptContext.CleanupLevel} email={promptContext.EmailLayout} dictionary={dictionary.Count}");

        var request = new GenerateContentRequest
        {
            Contents =
            [
                new RequestContent
                {
                    Role = "user",
                    Parts = [new RequestPart { Text = GeminiPrompt.Build(promptContext, _styleNotes, dictionary) }, audioPart],
                },
            ],
            GenerationConfig = new GenerationConfig { ThinkingConfig = new ThinkingConfig { ThinkingLevel = _thinkingLevel } },
        };

        // The primary model, then the backup if it is busy or gives no first answer in time (ticket 60).
        string text = await ModelAttempts.RunAsync(
            _models, ModelAttempts.AudioDeadline(wavBytes.Length),
            (model, token) => AskAsync(model, request, RequestTimeoutFor(wavBytes.Length), token),
            "Online Dictation (Gemini)", cancellationToken).ConfigureAwait(true);

        string trimmed = text.Trim();
        if (trimmed == NoSpeechSentinel)
        {
            return null;
        }

        (string finalText, string? original) = GeminiOutput.Split(trimmed);
        if (original is not null)
        {
            _ledger?.Record(original);
        }

        return PasteLayout.Finish(finalText, promptContext, word => BuiltInVocabulary.Shared.IsOrdinaryWord(word));
    }

    /// <summary>One generateContent call with <paramref name="model"/>; the text it answered.</summary>
    private async Task<string> AskAsync(string model, GenerateContentRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{model}:generateContent");
        httpRequest.Headers.Add("x-goog-api-key", _apiKey());
        httpRequest.Content = JsonContent.Create(request);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, deadline.Token).ConfigureAwait(true);
        string body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(true);

        if (!response.IsSuccessStatusCode)
        {
            throw new OnlineServiceException(response.StatusCode, ExtractErrorMessage(body), OnlineProvider.Gemini);
        }

        GenerateContentResponse? parsed = JsonSerializer.Deserialize<GenerateContentResponse>(body);
        string? text = parsed?.Candidates?
            .FirstOrDefault()?.Content?.Parts?
            .FirstOrDefault(p => p.Text is not null)?.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            string reason = parsed?.PromptFeedback?.BlockReason ?? "empty response";
            throw new OnlineServiceException(response.StatusCode, $"Gemini returned no text ({reason}).", OnlineProvider.Gemini);
        }

        return text;
    }

    /// <summary>The configured model, then the preset table's backup.</summary>
    internal static IReadOnlyList<string> ModelsFor(string primary) =>
        primary == OnlinePresets.GeminiBackup ? [primary, OnlinePresets.GeminiModel] : [primary, OnlinePresets.GeminiBackup];

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
