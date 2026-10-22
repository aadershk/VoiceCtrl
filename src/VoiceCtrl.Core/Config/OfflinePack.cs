namespace VoiceCtrl.Core.Config;

/// <summary>
/// The one extra Offline language pack on top of the English model Setup installs (spec §5.3).
/// Only one is installed at a time; picking another replaces it. Saved in settings.json.
/// </summary>
public enum OfflinePack
{
    /// <summary>English only: the model from Setup.</summary>
    None,

    /// <summary>About 25 European languages, as fast as English.</summary>
    European,

    /// <summary>About 99 languages, including Hindi and Tamil; slower.</summary>
    AllLanguages,
}
