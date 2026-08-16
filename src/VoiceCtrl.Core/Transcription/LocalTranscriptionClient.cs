using System.IO;
using System.Net.Http;
using System.Timers;
using NAudio.Wave;
using SherpaOnnx;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
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
    private const string HuggingFaceRepo = "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
    private static readonly string[] ModelFileNames =
        ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly string _modelDirectory;
    private readonly SemaphoreSlim _recognizerLock = new(1, 1);
    private readonly Timer _idleUnloadTimer;
    private readonly HttpClient _httpClient = new();

    private OfflineRecognizer? _recognizer;
    private bool _disposed;

    public LocalTranscriptionClient(AppConfig config)
    {
        string modelsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceCtrl", "models");
        _modelDirectory = Path.Combine(modelsRoot, config.LocalModelVariant);

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

        return OfflineTextPostProcessor.Clean(rawText);
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
        config.ModelConfig.NumThreads = 2;
        config.DecodingMethod = "greedy_search";

        SimpleFileLogger.LogInfo("Loading offline transcription model...");
        _recognizer = new OfflineRecognizer(config);
        SimpleFileLogger.LogInfo("Offline transcription model loaded.");
        return _recognizer;
    }

    private async Task EnsureModelDownloadedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_modelDirectory);

        foreach (string fileName in ModelFileNames)
        {
            string destinationPath = Path.Combine(_modelDirectory, fileName);
            if (File.Exists(destinationPath))
            {
                continue;
            }

            SimpleFileLogger.LogInfo($"Downloading offline model file {fileName} (one-time, ~650-700MB total)...");

            string url = $"https://huggingface.co/{HuggingFaceRepo}/resolve/main/{fileName}";
            string tempPath = destinationPath + ".download";

            using (HttpResponseMessage response = await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                await using (FileStream fileStream = File.Create(tempPath))
                {
                    await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            SimpleFileLogger.LogInfo($"Downloaded offline model file {fileName}.");
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

        var samples = new List<float>();
        float[] buffer = new float[4096];
        int read;
        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        return samples.ToArray();
    }

    private void ResetIdleTimer()
    {
        _idleUnloadTimer.Stop();
        _idleUnloadTimer.Start();
    }

    private async void OnIdleTimerElapsed(object? sender, ElapsedEventArgs e)
    {
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
