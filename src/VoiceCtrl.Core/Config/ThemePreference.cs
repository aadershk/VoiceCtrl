namespace VoiceCtrl.Core.Config;

/// <summary>
/// The user's stored Theme choice. Distinct from <see cref="Theme"/>, the resolved Light-or-Dark
/// value <see cref="ThemeResolver"/> computes from this preference plus the live OS theme — the
/// same stored-preference-vs-resolved-runtime-value split the codebase already has for transcription
/// Mode (<see cref="TranscriptionModePreference.Auto"/> resolves elsewhere; <see cref="ThemePreference.System"/>
/// resolves in <see cref="ThemeResolver"/>).
/// </summary>
public enum ThemePreference
{
    Light,
    Dark,

    /// <summary>Follows the live Windows light/dark setting. The default for a first run.</summary>
    System,
}
