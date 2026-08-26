using System.IO;
using System.Net.Http;
using System.Timers;
using NAudio.Wave;
using SherpaOnnx;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;
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
public sealed class LocalTranscriptionClient : ITranscriptionClient
{
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly string _modelDirectory;
    private readonly int _numThreads;
    private readonly Func<bool>? _shouldStayResident;
    private readonly PersonalizationStore _personalization;
    private readonly bool _spokenPunctuation;
    private readonly SemaphoreSlim _recognizerLock = new(1, 1);
    private readonly Timer _idleUnloadTimer;
    private readonly HttpClient _httpClient = new();

    private OfflineRecognizer? _recognizer;
    private bool _disposed;

    /// <param name="shouldStayResident">
    /// Consulted when the idle timer fires, to decide whether to keep the model in memory.
    /// Loading it costs several seconds, which someone who has deliberately chosen Offline mode
    /// would otherwise pay every time they stop dictating for five minutes. In Auto that trade is
    /// the wrong way round, since the model may never be reached at all while the network is up,
    /// and holding ~700MB for a path nothing uses is worse than a reload nobody waits for.
    /// Null keeps the unconditional unload.
    /// </param>
    public LocalTranscriptionClient(
        AppConfig config,
        Func<bool>? shouldStayResident = null,
        PersonalizationStore? personalization = null)
    {
        _modelDirectory = Path.Combine(UserDataPaths.Models, config.LocalModelVariant);
        _numThreads = config.LocalNumThreads;
        _shouldStayResident = shouldStayResident;
        _personalization = personalization ?? PersonalizationStore.Empty;
        _spokenPunctuation = config.SpokenPunctuation;

        _idleUnloadTimer = new Timer(IdleUnloadDelay.TotalMilliseconds) { AutoReset = false };
        _idleUnloadTimer.Elapsed += OnIdleTimerElapsed;
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        float[] samples = ReadMonoFloatSamples(wavBytes);
        string rawText;

        await _recognizerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OfflineRecognizer recognizer = await EnsureRecognizerLoadedNoLockAsync(cancellationToken).ConfigureAwait(false);

            using OfflineStream stream = recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            recognizer.Decode(stream);
            rawText = stream.Result.Text;
        }
        finally
        {
            _recognizerLock.Release();
        }

        ResetIdleTimer();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        string cleaned = OfflineTextPostProcessor.Clean(rawText, _spokenPunctuation);

        // Applied here rather than alongside the snippet expansion, because it only makes sense
        // for this path. Online gets the same dictionary as a prompt hint, where the model can
        // check a candidate against the audio instead of guessing from the text alone.
        return DictionaryCorrector.Apply(cleaned, _personalization.DictionaryTerms);
    }

    /// <summary>
    /// For the local provider, "prewarming" means loading the model (and downloading it, on first
    /// ever use) ahead of when the user stops recording, rather than warming an HTTP connection.
    /// </summary>
    public async Task PrewarmConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
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
        if (_recognizer is { } existing)
        {
            return existing;
        }

        await EnsureModelDownloadedAsync(cancellationToken).ConfigureAwait(false);

        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Transducer.Encoder = Path.Combine(_modelDirectory, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(_modelDirectory, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(_modelDirectory, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(_modelDirectory, "tokens.txt");
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = _numThreads;
        config.DecodingMethod = "greedy_search";

        SimpleFileLogger.LogInfo("Loading offline transcription model...");
        _recognizer = new OfflineRecognizer(config);
        SimpleFileLogger.LogInfo("Offline transcription model loaded.");
        return _recognizer;
    }

    private Task EnsureModelDownloadedAsync(CancellationToken cancellationToken) =>
        LocalModelDownloader.DownloadAllAsync(_modelDirectory, _httpClient, progress: null, cancellationToken);

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

        await _recognizerLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
            SimpleFileLogger.LogInfo("Offline transcription model unloaded after idle timeout.");
        }
        finally
        {
            _recognizerLock.Release();
        }
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
        _httpClient.Dispose();
        _recognizerLock.Dispose();
    }
}
