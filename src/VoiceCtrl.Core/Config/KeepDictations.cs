namespace VoiceCtrl.Core.Config;

/// <summary>Keep Dictations (CONTEXT: Retention): how many Dictations stay on this PC as text.
/// Audio is never kept, whichever is chosen.</summary>
public enum KeepDictations
{
    /// <summary>The last 50 Dictations. The default.</summary>
    Last50,

    /// <summary>Only Dictations from the last 24 hours.</summary>
    Last24Hours,

    /// <summary>Nothing on disk. Paste Again still works from memory until VoiceCtrl quits.</summary>
    None,
}
