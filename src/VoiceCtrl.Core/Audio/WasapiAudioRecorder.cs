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

    private WasapiCapture? _capture;
    private MemoryStream? _rawBuffer;
    private WaveFileWriter? _rawWriter;
    private TaskCompletionSource<bool>? _stoppedTcs;
    private DateTime _startedAtUtc;

    public bool IsRecording { get; private set; }

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

        _startedAtUtc = DateTime.UtcNow;
        IsRecording = true;
        _capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _rawWriter?.Write(e.Buffer, 0, e.BytesRecorded);
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

        byte[] resampledWavBytes = ResampleTo16BitMono16k(rawWavBytes, rawFormat);
        return new AudioClip(resampledWavBytes, duration);
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
