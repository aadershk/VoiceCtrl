namespace VoiceCtrl.Core.Audio;

/// <summary>
/// A captured, already-resampled (16-bit PCM mono) dictation clip.
/// </summary>
public sealed class AudioClip
{
    private static readonly TimeSpan MinimumSpeechDuration = TimeSpan.FromMilliseconds(300);

    public byte[] WavBytes { get; }
    public TimeSpan Duration { get; }

    public AudioClip(byte[] wavBytes, TimeSpan duration)
    {
        WavBytes = wavBytes;
        Duration = duration;
    }

    /// <summary>
    /// Cheap local short-circuit before ever calling the transcription API. Catches only an
    /// accidental double-click with no real recording. Deliberately duration-only: a peak/mean
    /// amplitude check was tried and measured against real hardware, where a 2s room-tone sample
    /// peaked at 418/32767 while a real, cleanly-transcribed speech sample peaked at only
    /// 297/32767, so no fixed amplitude threshold classifies both correctly, so amplitude isn't a
    /// usable signal here. Gemini's own [NO_SPEECH_DETECTED] sentinel is the sole content-based
    /// silence detector.
    /// </summary>
    public bool IsLikelySilent() => Duration < MinimumSpeechDuration;

    /// <summary>
    /// The device delivered no signal at all: every sample within a few least-significant bits of
    /// zero, which is what a muted or dead input gives (VED-75, "No audio from the microphone").
    /// Real hardware, even in a quiet room, sits far above this (room tone peaked at 418 of 32767),
    /// so unlike a speech threshold this can't mistake quiet talking for a dead mic. False for a
    /// clip too short to judge and for bytes that are not a 16-bit WAV.
    /// </summary>
    public bool HasNoSignal()
    {
        const int wavHeaderBytes = 44;
        const int noSignalPeak = 3;

        if (IsLikelySilent() || WavBytes.Length < wavHeaderBytes + 2 * MinimumJudgedSamples)
        {
            return false;
        }

        ReadOnlySpan<byte> pcm = WavBytes.AsSpan(wavHeaderBytes);
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            if (Math.Abs((int)BitConverter.ToInt16(pcm.Slice(i, 2))) > noSignalPeak)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>100 ms at 16 kHz: fewer samples than this say nothing about the device.</summary>
    private const int MinimumJudgedSamples = 1600;
}
