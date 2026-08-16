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
}
