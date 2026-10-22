using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Audio;

/// <summary>
/// The Settings microphone row's live level (spec §5.3): listens to one device and reports its
/// level, and nothing is kept. It runs only while the row is on screen, because an open microphone
/// shows Windows' "in use" indicator. A second shared-mode capture beside a Dictation's is fine.
/// </summary>
public sealed class MicrophoneLevelPreview : IDisposable
{
    /// <summary>The same 20 Hz as the recorder's level: smooth enough for three bars.</summary>
    private const int LevelIntervalMs = 50;

    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private bool _isFloat;
    private int _bitsPerSample;
    private long _lastLevelTick;

    /// <summary>0..1, about 20 times a second, on the capture thread.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>Starts listening to <paramref name="deviceId"/> (null: the Windows default),
    /// replacing any device it was listening to.</summary>
    public void Start(string? deviceId)
    {
        lock (_gate)
        {
            StopCapture();
            try
            {
                var capture = new WasapiCapture(CaptureDevices.Open(deviceId));
                WaveFormat format = capture.WaveFormat;
                _isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
                    || (format is WaveFormatExtensible extensible && IsFloat(extensible));
                _bitsPerSample = format.BitsPerSample;
                capture.DataAvailable += OnDataAvailable;
                capture.StartRecording();
                _capture = capture;
            }
            catch (COMException ex)
            {
                // No microphone, or one another app holds in exclusive mode: the bars just stay still.
                SimpleFileLogger.LogError("Microphone level preview", ex);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCapture();
        }
    }

    private static bool IsFloat(WaveFormatExtensible format)
    {
        try
        {
            return format.ToStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        long now = Environment.TickCount64;
        if (now - _lastLevelTick < LevelIntervalMs)
        {
            return;
        }

        _lastLevelTick = now;
        LevelChanged?.Invoke(AudioLevelMeter.ComputeLevel(e.Buffer.AsSpan(0, e.BytesRecorded), _isFloat, _bitsPerSample));
    }

    private void StopCapture()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= OnDataAvailable;
        try
        {
            _capture.StopRecording();
        }
        catch (COMException)
        {
            // The device went away mid-listen.
        }

        _capture.Dispose();
        _capture = null;
    }

    public void Dispose() => Stop();
}
