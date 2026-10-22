namespace VoiceCtrl.Core.Config;

/// <summary>
/// The user's live, tray-switchable transcription mode choice, kept in the settings store's
/// transcription section and read through <see cref="TranscriptionModeStore"/>.
/// </summary>
public enum TranscriptionModePreference
{
    /// <summary>Online when the network looks available, Offline otherwise. See
    /// <see cref="VoiceCtrl.Core.Transcription.AdaptiveTranscriptionClient"/> for the exact rules.</summary>
    Auto,

    /// <summary>Always use Gemini, never falls back, so a real failure surfaces exactly as it does today.</summary>
    Online,

    /// <summary>Always use the local model, never calls out to Gemini.</summary>
    Offline,
}
