namespace VoiceCtrl.Core.Config;

/// <summary>
/// Which local language model Smart cleanup uses (ticket 37). Saved in settings.json.
/// </summary>
public enum SmartCleanupTier
{
    /// <summary>Qwen3-1.7B: about 1.1 GB, a dictation costs about 2 s more. Weaker at lists and fillers.</summary>
    Standard,

    /// <summary>Qwen3-4B-Instruct-2507: about 2.5 GB, about 4-6 s more on a typical laptop CPU, and
    /// clearly closer to Online's cleanup. The default: it is what the quality target needed.</summary>
    Better,
}
