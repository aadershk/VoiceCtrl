namespace VoiceCtrl.Core.Config;

/// <summary>
/// The user's live, tray-switchable transcription mode choice. Distinct from
/// <see cref="AppConfig.TranscriptionMode"/>, which is only the one-time .env value
/// <see cref="TranscriptionModeStore"/> reads to seed this preference on its very first run,
/// after that, the persisted preference is the sole source of truth and .env is no longer consulted.
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
