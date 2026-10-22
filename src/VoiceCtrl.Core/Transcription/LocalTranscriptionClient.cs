using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Timers;
using NAudio.Wave;
using SherpaOnnx;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Rewrite;
using Timer = System.Timers.Timer;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Offline transcription via a local Parakeet-TDT model (NeMo transducer) run through sherpa-onnx,
/// CPU-only: no audio leaves the machine, no API key needed. Model weights (~650-700MB, CC-BY-4.0,
/// see THIRD-PARTY-NOTICES.md) are not bundled; they're fetched once from Hugging Face into
/// %LocalAppData%\VoiceCtrl\models\ on first use and reused after that.
///
/// CPU-only is deliberate, not a fallback: it keeps VRAM usage at 0MB, so there's no contention with
/// a GPU doing other work (e.g. gaming) while a dictation clip is transcribed.
/// </summary>
public sealed class LocalTranscriptionClient : ITranscriptionClient, IInstructionTranscriber
{
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly Func<OfflineModel> _modelSource;
    private readonly string _modelsRoot;
    private readonly int _numThreads;
    private readonly Func<bool>? _shouldStayResident;
    private readonly Func<OfflineRecognizerConfig, OfflineRecognizer> _recognizerFactory;
    private readonly PersonalizationStore _personalization;
    private readonly bool _spokenPunctuation;
    private readonly SpeechConditioning _conditioning;
    private readonly SmartCleanupService? _smartCleanup;
    private readonly SmartCleanupLedger? _ledger;
    private readonly EngineRecorder? _engine;
    private readonly Func<string> _cleanupLevel;
    private readonly Func<bool> _pressEnter;
    private readonly Func<StyleSettings> _styles;
    private readonly Func<BuiltInVocabulary> _builtIn;
    private readonly Func<string?> _foregroundProcessName;
    private readonly Func<string?> _foregroundWindowTitle;
    private readonly AppConfig _config;
    private readonly SemaphoreSlim _recognizerLock = new(1, 1);
    private readonly Timer _idleUnloadTimer;
    private readonly HttpClient _httpClient = new();

    private OfflineRecognizer? _recognizer;
    private string? _loadedDirectory;

    // What the loaded recognizer was biased toward (null: greedy). A Dictionary change reloads it, since
    // sherpa reads the hotwords when it is built; the reload happens in Prewarm, while the speaker talks.
    private string? _loadedHotwords;
    private string? _loadedModelDirectory;
    private OfflineModel? _loadedModel;
    private OfflineRecognizer? _greedyFallback;
    private bool _disposed;

    /// <param name="shouldStayResident">
    /// Consulted when the idle timer fires, to decide whether to keep the model in memory.
    /// Loading it costs several seconds, which someone who has deliberately chosen Offline mode
    /// would otherwise pay every time they stop dictating for five minutes. In Auto that trade is
    /// the wrong way round, since the model may never be reached at all while the network is up,
    /// and holding ~700MB for a path nothing uses is worse than a reload nobody waits for.
    /// Null keeps the unconditional unload.
    /// </param>
    /// <param name="recognizerFactory">
    /// Constructs the recognizer from config. Defaults to the real constructor; overridable so
    /// tests can observe construction (e.g. which thread it runs on) without loading a real model.
    /// </param>
    /// <param name="modelSource">Asked before every use which model to run, so installing or
    /// removing a language pack takes effect on the next Dictation without a restart. Defaults to
    /// the English model in <paramref name="config"/>'s folder.</param>
    /// <param name="modelsRoot">Where model folders live; <c>UserDataPaths.Models</c> unless a test says otherwise.</param>
    /// <param name="smartCleanup">The local language model that edits the text after the rules, or null
    /// for the rules alone (ticket 37).</param>
    /// <param name="ledger">Told the text Smart cleanup replaced, for the Dictations screen's Original.</param>
    /// <param name="cleanupLevel">The default Cleanup Level, read at each Dictation; Smart cleanup and the
    /// Online prompt resolve Profiles the same way.</param>
    /// <param name="pressEnter">The Press Enter setting, read at each Dictation.</param>
    /// <param name="styles">The Style screen's settings, read at each Dictation.</param>
    /// <param name="foregroundProcessName">The app being dictated into; defaults to the foreground window.</param>
    /// <param name="foregroundWindowTitle">Its window's title, which names a browser's site. Defaults to the
    /// foreground window's, or to none when <paramref name="foregroundProcessName"/> is given.</param>
    /// <param name="builtInVocabulary">The hidden list of names and terms (ticket 36b), fetched at each use so it is only loaded when needed; empty unless the app passes it.</param>
    /// <param name="engineRecorder">The opt-in engine record (VED-77): told, per Dictation, what the engine did. It records nothing while its switch is off.</param>
    public LocalTranscriptionClient(
        AppConfig config,
        Func<bool>? shouldStayResident = null,
        PersonalizationStore? personalization = null,
        Func<OfflineRecognizerConfig, OfflineRecognizer>? recognizerFactory = null,
        Func<OfflineModel>? modelSource = null,
        string? modelsRoot = null,
        SpeechConditioning conditioning = SpeechConditioning.TrimSilence,
        SmartCleanupService? smartCleanup = null,
        SmartCleanupLedger? ledger = null,
        Func<string>? cleanupLevel = null,
        Func<bool>? pressEnter = null,
        Func<string?>? foregroundProcessName = null,
        Func<BuiltInVocabulary>? builtInVocabulary = null,
        EngineRecorder? engineRecorder = null,
        Func<string?>? foregroundWindowTitle = null,
        Func<StyleSettings>? styles = null)
    {
        _config = config;
        _engine = engineRecorder;
        _builtIn = builtInVocabulary ?? (() => BuiltInVocabulary.Empty);
        _smartCleanup = smartCleanup;
        _ledger = ledger;
        _cleanupLevel = cleanupLevel ?? (() => config.CleanupLevel);
        _pressEnter = pressEnter ?? (() => false);
        _styles = styles ?? (() => new StyleSettings());
        _foregroundProcessName = foregroundProcessName ?? ForegroundAppDetector.GetForegroundProcessName;
        _foregroundWindowTitle = foregroundWindowTitle ??
                                 (foregroundProcessName is null ? ForegroundAppDetector.GetForegroundWindowTitle : () => null);
        _modelSource = modelSource ?? (() => OfflineModels.English with { Directory = config.LocalModelVariant });
        _modelsRoot = modelsRoot ?? UserDataPaths.Models;
        _numThreads = config.LocalNumThreads;
        _shouldStayResident = shouldStayResident;
        _recognizerFactory = recognizerFactory ?? (recognizerConfig => new OfflineRecognizer(recognizerConfig));
        _personalization = personalization ?? PersonalizationStore.Empty;
        _spokenPunctuation = config.SpokenPunctuation;
        _conditioning = conditioning;

        _idleUnloadTimer = new Timer(IdleUnloadDelay.TotalMilliseconds) { AutoReset = false };
        _idleUnloadTimer.Elapsed += OnIdleTimerElapsed;
    }

    /// <summary>The words of a spoken instruction for a rewrite (ticket 39): the rules' clean-up and
    /// the Dictionary's spellings, but no Smart cleanup, which edits Dictations.</summary>
    public async Task<string?> TranscribeInstructionAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        string rawText = await RecognizeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        return ApplySpellings(OfflineTextPostProcessor.Clean(rawText, _spokenPunctuation));
    }

    Task IInstructionTranscriber.PrewarmAsync(CancellationToken cancellationToken) => PrewarmConnectionAsync(cancellationToken);

    /// <summary>The speech model's raw text for a clip. Resets the idle unload timer.</summary>
    private async Task<string> RecognizeAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        float[] samples = SpeechConditioner.Condition(ReadMonoFloatSamples(wavBytes), _conditioning);
        string rawText;

        await _recognizerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OfflineRecognizer recognizer = await EnsureRecognizerLoadedNoLockAsync(cancellationToken).ConfigureAwait(false);

            using OfflineStream stream = recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            recognizer.Decode(stream);
            rawText = stream.Result.Text;

            // Beam search can answer nothing or "Yeah." for real speech (upstream bug); greedy does not.
            if (_loadedHotwords is not null && HotwordBias.LooksLikeBeamFailure(rawText) && samples.Length >= MinSamplesForFallback)
            {
                SimpleFileLogger.LogInfo("Hotword decode came back empty; decoding again without it.");
                rawText = DecodeGreedyNoLock(samples);
            }
        }
        finally
        {
            _recognizerLock.Release();
        }

        ResetIdleTimer();
        return rawText;
    }

    /// <summary>1.5 s of audio at 16 kHz: shorter than this after the silence trim, an empty answer is the truth.</summary>
    private const int MinSamplesForFallback = 24000;

    /// <summary>Caller must hold <see cref="_recognizerLock"/>. The second recognizer is built on first need, since the bug is rare.</summary>
    private string DecodeGreedyNoLock(float[] samples)
    {
        _greedyFallback ??= _recognizerFactory(BuildRecognizerConfig(_loadedModel!, _loadedModelDirectory!, _numThreads));
        using OfflineStream stream = _greedyFallback.CreateStream();
        stream.AcceptWaveform(16000, samples);
        _greedyFallback.Decode(stream);
        return stream.Result.Text;
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        Stopwatch clock = Stopwatch.StartNew();
        string rawText = await RecognizeAsync(wavBytes, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        return await TranscribeRecordedAsync(rawText, cancellationToken, clock).ConfigureAwait(false);
    }

    /// <summary>Everything after the speech model: the rules, the Dictionary, the built-in list, Mend
    /// and Smart cleanup. Leaves the engine record (if it is on) what the engine did, as counts, word
    /// pairs and timings; <paramref name="clock"/> started with the Dictation's transcription.</summary>
    internal async Task<string> TranscribeRecordedAsync(string rawText, CancellationToken cancellationToken, Stopwatch? clock = null)
    {
        clock ??= Stopwatch.StartNew();
        EngineTrace? trace = _engine?.Begin();
        string text = await RefineAsync(rawText, trace, cancellationToken).ConfigureAwait(false);
        if (trace is not null)
        {
            _engine!.Complete(trace, clock.ElapsedMilliseconds);
        }

        return text;
    }

    private async Task<string> RefineAsync(string rawText, EngineTrace? trace, CancellationToken cancellationToken)
    {
        string cleaned = OfflineTextPostProcessor.Clean(rawText, _spokenPunctuation);

        // Applied here rather than alongside the snippet expansion, because it only makes sense
        // for this path. Online gets the same dictionary as a prompt hint, where the model can
        // check a candidate against the audio instead of guessing from the text alone.
        string ruleCleaned = ApplySpellings(cleaned, trace);

        PromptContext context = SmartCleanupContext();
        if (_smartCleanup is not { IsAvailable: true })
        {
            return PasteLayout.Finish(ruleCleaned, context, _builtIn().IsOrdinaryWord);
        }

        // The rules came first and stay the fallback: this only ever improves on ruleCleaned. Words the
        // sentence shows to be wrong are mended before the cleanup reads the text, so it works on
        // the corrected words.
        string mended = await _smartCleanup
            .Mend(ruleCleaned, context, _personalization.Dictionary, _builtIn(), cancellationToken, trace)
            .ConfigureAwait(false);
        SmartCleanupResult smart = await _smartCleanup
            .Clean(mended, context, _config.StyleNotes, DictionaryWithReferences(mended), cancellationToken, trace)
            .ConfigureAwait(false);
        if (smart.Applied || mended != ruleCleaned)
        {
            _ledger?.Record(ruleCleaned);
        }

        return PasteLayout.Finish(smart.Text, context, _builtIn().IsOrdinaryWord);
    }

    /// <summary>The Dictionary's spellings first, then the built-in list's on whatever the speech
    /// model still plainly did not know. The user's own words always come first and are never
    /// overruled by the list.</summary>
    private string ApplySpellings(string text, EngineTrace? trace = null)
    {
        IReadOnlyList<DictionaryEntry> dictionary = _personalization.Dictionary;
        string corrected = DictionaryCorrector.Apply(text, dictionary);
        List<(string Heard, string Term)>? changes = trace is null ? null : [];
        string spelled = _builtIn().Apply(corrected, PersonalWords(dictionary), changes);
        if (changes is not null)
        {
            trace!.Vocabulary.AddRange(changes.Select(change => new WordPair(change.Heard, change.Term)));
        }

        // "an data" is a recogniser slip no sentence is needed to see; Word for word keeps every word.
        return _cleanupLevel() == CleanupLevels.Verbatim ? spelled : ArticleAgreement.Apply(spelled, _builtIn().IsOrdinaryWord);
    }

    /// <summary>What Smart cleanup is told to spell: the Dictionary, then the few built-in terms that
    /// sound like words still unexplained in this one dictation.</summary>
    private IReadOnlyList<DictionaryEntry> DictionaryWithReferences(string text)
    {
        IReadOnlyList<DictionaryEntry> dictionary = _personalization.Dictionary;
        IReadOnlyList<DictionaryEntry> references = _builtIn().Suggest(text, MaxBuiltInReferences, PersonalWords(dictionary));
        return references.Count == 0 ? dictionary : [.. dictionary, .. references];
    }

    private static IReadOnlyCollection<string> PersonalWords(IReadOnlyList<DictionaryEntry> dictionary) =>
        dictionary.SelectMany(entry => entry.AlsoHeardAs.Prepend(entry.Word)).ToList();

    /// <summary>Few enough that the prompt stays short and the model is not handed a list to pick from.</summary>
    private const int MaxBuiltInReferences = 8;

    /// <summary>The Cleanup Level, tone and layout rules for the app in front, from its Profile.</summary>
    private PromptContext SmartCleanupContext()
    {
        string? processName = _foregroundProcessName();
        return GeminiPrompt.ContextFor(
            _personalization.Profiles.Resolve(processName), processName,
            new PromptDefaults(_cleanupLevel(), _config.EnableToneAwareness, _config.PromptStyleApps, _config.ChatStyleApps, _pressEnter(), _styles()),
            _foregroundWindowTitle());
    }

    /// <summary>
    /// For the local provider, "prewarming" means loading the model (and downloading it, on first
    /// ever use) ahead of when the user stops recording, rather than warming an HTTP connection.
    /// </summary>
    public async Task PrewarmConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Started first: the language model's load and its first read of the rules overlap with
            // the speech model's, and with the user speaking.
            Task primeLanguageModel = _smartCleanup is { IsAvailable: true }
                ? _smartCleanup.PrimeAsync(SmartCleanupContext(), _config.StyleNotes, _personalization.Dictionary, cancellationToken)
                : Task.CompletedTask;

            await _recognizerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureRecognizerLoadedNoLockAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _recognizerLock.Release();
            }

            ResetIdleTimer();
            await primeLanguageModel.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort per the interface contract. TranscribeAsync will retry and surface any real failure.
            SimpleFileLogger.LogError("OfflinePrewarm", ex);
        }
    }

    /// <summary>Caller must hold <see cref="_recognizerLock"/>.</summary>
    private async Task<OfflineRecognizer> EnsureRecognizerLoadedNoLockAsync(CancellationToken cancellationToken)
    {
        OfflineModel model = _modelSource();
        IReadOnlyList<string> terms = HotwordBias.TermsFor(model, _personalization.Dictionary);
        string? hotwords = terms.Count > 0 ? string.Join('\n', terms) : null;
        if (_recognizer is { } existing)
        {
            if (_loadedDirectory == model.Directory && _loadedHotwords == hotwords)
            {
                return existing;
            }

            // A language pack was installed or removed, or the Dictionary changed: drop the model that is loaded.
            SimpleFileLogger.LogInfo("Offline model or Dictionary changed; unloading the previous model.");
            existing.Dispose();
            _recognizer = null;
            DisposeGreedyFallback();
        }

        string modelDirectory = model.PathUnder(_modelsRoot);
        await LocalModelDownloader.DownloadAllAsync(modelDirectory, _httpClient, progress: null, cancellationToken, model: model)
            .ConfigureAwait(false);

        HotwordBias? bias = terms.Count > 0 ? HotwordBias.Write(modelDirectory, terms) : null;
        OfflineRecognizerConfig config = BuildRecognizerConfig(model, modelDirectory, _numThreads, bias);

        SimpleFileLogger.LogInfo("Loading offline transcription model...");

        // Explicitly offloaded rather than left to the awaits above: when the model is already on
        // disk, EnsureModelDownloadedAsync's file-exists checks never actually suspend, so without
        // this the whole chain - including this multi-second, several-hundred-MB native load -
        // would otherwise run synchronously on whatever thread called in, which is the UI thread
        // on the "start Dictation" path and would freeze the app (and its global keyboard hook).
        _recognizer = await Task.Run(() => _recognizerFactory(config), cancellationToken).ConfigureAwait(false);
        _loadedDirectory = model.Directory;
        _loadedHotwords = hotwords;
        _loadedModel = model;
        _loadedModelDirectory = modelDirectory;

        SimpleFileLogger.LogInfo("Offline transcription model loaded.");
        return _recognizer;
    }

    private void DisposeGreedyFallback()
    {
        _greedyFallback?.Dispose();
        _greedyFallback = null;
    }

    /// <summary>How sherpa-onnx is set up for <paramref name="model"/>, whose files are in <paramref name="directory"/>.
    /// With a <paramref name="bias"/> (English only) it decodes with beam search toward the Dictionary's words.</summary>
    internal static OfflineRecognizerConfig BuildRecognizerConfig(OfflineModel model, string directory, int numThreads, HotwordBias? bias = null)
    {
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Tokens = Path.Combine(directory, "tokens.txt");
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = numThreads;
        config.DecodingMethod = "greedy_search";

        if (bias is not null)
        {
            config.DecodingMethod = "modified_beam_search";
            config.MaxActivePaths = 4;
            config.HotwordsFile = bias.HotwordsFile;
            config.HotwordsScore = HotwordBias.Score;
            config.ModelConfig.ModelingUnit = "bpe";
            config.ModelConfig.BpeVocab = bias.BpeVocabFile;
        }

        if (model.Kind == OfflineModelKind.Whisper)
        {
            config.ModelConfig.Whisper.Encoder = Path.Combine(directory, "encoder.int8.onnx");
            config.ModelConfig.Whisper.Decoder = Path.Combine(directory, "decoder.int8.onnx");

            // Empty: Whisper detects the language itself, which is the point of this pack.
            config.ModelConfig.Whisper.Language = "";
            config.ModelConfig.Whisper.Task = "transcribe";
        }
        else
        {
            config.ModelConfig.Transducer.Encoder = Path.Combine(directory, "encoder.int8.onnx");
            config.ModelConfig.Transducer.Decoder = Path.Combine(directory, "decoder.int8.onnx");
            config.ModelConfig.Transducer.Joiner = Path.Combine(directory, "joiner.int8.onnx");
            config.ModelConfig.ModelType = "nemo_transducer";
        }

        return config;
    }

    /// <summary>Lets go of the loaded model (waiting for a Dictation in progress), so its files can
    /// be deleted. The next use loads it again.</summary>
    public async Task UnloadAsync()
    {
        await _recognizerLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
            DisposeGreedyFallback();
        }
        finally
        {
            _recognizerLock.Release();
        }
    }

    /// <summary>
    /// The recorder always produces 16-bit/16kHz/mono WAV (see WasapiAudioRecorder), which is
    /// exactly what Parakeet expects, so this only needs to decode PCM16 to normalized float32,
    /// no resampling or channel mixing.
    /// </summary>
    private static float[] ReadMonoFloatSamples(byte[] wavBytes)
    {
        using var memoryStream = new MemoryStream(wavBytes);
        using var waveReader = new WaveFileReader(memoryStream);
        ISampleProvider sampleProvider = waveReader.ToSampleProvider();

        // Sized up front from the data length, which is exact for PCM, instead of growing a
        // List<float>: a 40-second clip is 640k samples, so the old path paid about twenty
        // reallocations and a full copy on ToArray() before the decoder saw anything.
        // Derived from Length rather than SampleCount because SampleCount throws on formats it
        // cannot count, and this must never be the thing that fails a transcription.
        int bytesPerSample = waveReader.WaveFormat.BitsPerSample / 8;
        int expectedSamples = bytesPerSample > 0 ? (int)(waveReader.Length / bytesPerSample) : 0;

        float[] samples = new float[expectedSamples];
        int total = 0;
        int read;
        while (total < samples.Length &&
               (read = sampleProvider.Read(samples, total, samples.Length - total)) > 0)
        {
            total += read;
        }

        // Exact in practice. A short read means a truncated or malformed WAV, and trimming beats
        // handing the decoder a tail of silence it would spend time transcribing.
        return total == samples.Length ? samples : samples[..total];
    }

    private void ResetIdleTimer()
    {
        _idleUnloadTimer.Stop();
        _idleUnloadTimer.Start();
    }

    private async void OnIdleTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_shouldStayResident?.Invoke() == true)
        {
            // Re-armed rather than left off, so that switching away from Offline later still
            // reaches an unload without needing another transcription to restart the timer.
            ResetIdleTimer();
            return;
        }

        await UnloadAsync().ConfigureAwait(false);
        SimpleFileLogger.LogInfo("Offline transcription model unloaded after idle timeout.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idleUnloadTimer.Elapsed -= OnIdleTimerElapsed;
        _idleUnloadTimer.Dispose();
        _recognizer?.Dispose();
        DisposeGreedyFallback();
        _httpClient.Dispose();
        _recognizerLock.Dispose();
    }
}
