using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NAudio.Wave;

namespace VoiceCtrl.Core.Audio;

/// <summary>
/// Captures microphone audio via WASAPI at the device's native format, then resamples the
/// complete clip to 16-bit/16kHz/mono once recording stops. Resampling after the fact (rather
/// than live, per callback) avoids feeding a streaming resampler irregularly-sized chunks,
/// which risks audible artifacts at chunk boundaries. Recording itself is just an append, so
/// it can't glitch.
/// </summary>
public sealed class WasapiAudioRecorder : IDisposable
{
    private const int TargetSampleRate = 16000;
    private const int TargetBits = 16;
    private const int TargetChannels = 1;

    /// <summary>WASAPI delivers buffers every ~10ms. Sampling the level every 50ms is four fewer
    /// passes over the audio per emission and still faster than the eye resolves.</summary>
    private const int LevelIntervalMs = 50;

    private WasapiCapture? _capture;
    private MemoryStream? _rawBuffer;
    private WaveFileWriter? _rawWriter;
    private TaskCompletionSource<bool>? _stoppedTcs;
    private DateTime _startedAtUtc;
    private bool _sourceIsFloat;
    private int _sourceBitsPerSample;
    private long _lastLevelTick;

    public bool IsRecording { get; private set; }

    /// <summary>
    /// Microphone level, 0..1, roughly 20 times a second while recording. Raised on the WASAPI
    /// capture thread, so a UI subscriber has to marshal it itself. Emitted only while a handler
    /// is attached, so the cost is zero when nothing is listening.
    /// </summary>
    public event Action<float>? LevelChanged;

    public void Start()
    {
        if (IsRecording)
        {
            return;
        }

        MMDevice device;
        using (var enumerator = new MMDeviceEnumerator())
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }

        _capture = new WasapiCapture(device);
        _rawBuffer = new MemoryStream();
        _rawWriter = new WaveFileWriter(new IgnoreDisposeStream(_rawBuffer), _capture.WaveFormat);
        _stoppedTcs = new TaskCompletionSource<bool>();

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        // Resolved once here rather than per buffer: it cannot change mid-recording, and the
        // WaveFormatExtensible unwrap below is the expensive part of asking.
        WaveFormat captureFormat = _capture.WaveFormat;
        _sourceIsFloat = IsFloatEncoded(captureFormat);
        _sourceBitsPerSample = captureFormat.BitsPerSample;
        _lastLevelTick = 0;

        _startedAtUtc = DateTime.UtcNow;
        IsRecording = true;
        _capture.StartRecording();
    }

    /// <summary>
    /// WASAPI shared mode usually reports its mix format as WAVE_FORMAT_EXTENSIBLE, whose own
    /// Encoding is just "Extensible" and says nothing about the samples. The real encoding is in
    /// the sub-format GUID, which is what ToStandardWaveFormat resolves. It throws for sub-formats
    /// it does not recognise, and an unreadable level indicator must not stop a recording, so an
    /// unknown format degrades to "not float" and AudioLevelMeter returns a flat zero for it.
    /// </summary>
    private static bool IsFloatEncoded(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        if (format is WaveFormatExtensible extensible)
        {
            try
            {
                return extensible.ToStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _rawWriter?.Write(e.Buffer, 0, e.BytesRecorded);

        Action<float>? handler = LevelChanged;
        if (handler is null)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (now - _lastLevelTick < LevelIntervalMs)
        {
            return;
        }

        _lastLevelTick = now;
        handler(AudioLevelMeter.ComputeLevel(
            e.Buffer.AsSpan(0, e.BytesRecorded), _sourceIsFloat, _sourceBitsPerSample));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stoppedTcs?.TrySetResult(true);
    }

    /// <summary>
    /// Stops recording and returns the captured audio resampled to 16-bit/16kHz/mono WAV bytes.
    /// </summary>
    public async Task<AudioClip> StopAsync()
    {
        if (!IsRecording || _capture is null || _rawWriter is null || _rawBuffer is null || _stoppedTcs is null)
        {
            throw new InvalidOperationException("Not currently recording.");
        }

        TimeSpan duration = DateTime.UtcNow - _startedAtUtc;

        _capture.StopRecording();
        // NAudio's StopRecording() call can return before the last buffers are flushed;
        // RecordingStopped is the actual completion signal.
        await _stoppedTcs.Task.ConfigureAwait(true);

        _rawWriter.Flush();
        WaveFormat rawFormat = _capture.WaveFormat;
        byte[] rawWavBytes = _rawBuffer.ToArray();

        CleanupCaptureResources();
        IsRecording = false;

        // Off the caller's thread: this is the one genuinely CPU-bound step in stopping, and the
        // caller is the WPF dispatcher, which is also drawing the overlay's switch to Processing.
        // Small in absolute terms (measured at 11ms for a 6s clip and 82ms for a 43s one), but
        // it is a stall the user sees precisely when they are watching for a response.
        byte[] resampledWavBytes = await Task.Run(
            () => ResampleTo16BitMono16k(rawWavBytes, rawFormat)).ConfigureAwait(true);

        return new AudioClip(resampledWavBytes, duration);
    }

    /// <summary>
    /// Stops recording and throws the audio away. Separate from <see cref="StopAsync"/> rather
    /// than a flag on it, because the expensive part of stopping is the resample, and a cancelled
    /// clip is never transcribed: skipping it makes Escape feel instant even on a long recording.
    /// Safe to call when not recording, since the point of a cancel is to reach a known-idle state.
    /// </summary>
    public async Task DiscardAsync()
    {
        if (!IsRecording || _capture is null || _stoppedTcs is null)
        {
            return;
        }

        _capture.StopRecording();
        await _stoppedTcs.Task.ConfigureAwait(true);

        CleanupCaptureResources();
        IsRecording = false;
    }

    private static byte[] ResampleTo16BitMono16k(byte[] rawWavBytes, WaveFormat rawFormat)
    {
        var targetFormat = new WaveFormat(TargetSampleRate, TargetBits, TargetChannels);

        // Already in the target format (e.g. a device that natively captures at 16kHz mono
        // 16-bit): resampling would be a costly no-op, and MediaFoundationResampler doesn't
        // like being asked to "resample" to an identical format.
        if (rawFormat.SampleRate == TargetSampleRate && rawFormat.BitsPerSample == TargetBits
            && rawFormat.Channels == TargetChannels && rawFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            return rawWavBytes;
        }

        using var rawStream = new MemoryStream(rawWavBytes);
        using var rawReader = new WaveFileReader(rawStream);
        using var resampler = new MediaFoundationResampler(rawReader, targetFormat)
        {
            ResamplerQuality = 60,
        };

        using var outBuffer = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(outBuffer), targetFormat))
        {
            byte[] chunk = new byte[targetFormat.AverageBytesPerSecond];
            int bytesRead;
            while ((bytesRead = resampler.Read(chunk, 0, chunk.Length)) > 0)
            {
                writer.Write(chunk, 0, bytesRead);
            }
        }

        return outBuffer.ToArray();
    }

    private void CleanupCaptureResources()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _rawWriter?.Dispose();
        _rawWriter = null;
        _rawBuffer = null;
        _stoppedTcs = null;
    }

    public void Dispose()
    {
        if (IsRecording && _capture is not null)
        {
            _capture.StopRecording();
        }

        CleanupCaptureResources();
    }
}
