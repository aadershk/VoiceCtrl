namespace VoiceCtrl.Core.Audio;

/// <summary>
/// The microphone as the Dictation engine sees it. An interface so every Dictation rule can be
/// unit tested against a fake, with no real audio device.
/// </summary>
public interface IAudioRecorder
{
    /// <summary>Microphone level, 0..1, while recording. Raised on the capture thread.</summary>
    event Action<float>? LevelChanged;

    /// <summary>Opens the capture device and starts recording. Throws if no microphone can be opened.</summary>
    void Start();

    /// <summary>Stops recording and returns the whole clip.</summary>
    Task<AudioClip> StopAsync();
}
