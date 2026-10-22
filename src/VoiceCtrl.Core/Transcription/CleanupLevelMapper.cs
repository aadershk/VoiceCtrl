namespace VoiceCtrl.Core.Transcription;

/// <summary>The Cleanup Levels, by the names settings.json and Profiles store. The Settings labels
/// are friendlier: <c>verbatim</c> is "Word for word" and <c>aggressive</c> is "Thorough".</summary>
public static class CleanupLevels
{
    public const string Verbatim = "verbatim";
    public const string Light = "light";
    public const string Standard = "standard";
    public const string Aggressive = "aggressive";

    public static IReadOnlyList<string> All { get; } = [Verbatim, Light, Standard, Aggressive];

    public static bool IsValid(string? level) => level is not null && All.Contains(level, StringComparer.Ordinal);
}

internal static class CleanupLevelMapper
{
    private static readonly Dictionary<string, string> Instructions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["verbatim"] = "Transcribe word for word. Keep every word the speaker said, including filler " +
            "words (\"um\", \"uh\"), repeats and false starts, and change nothing except adding " +
            "punctuation and capitalization. This wins over rule 3: keep retracted content as spoken.",
        ["light"] = "Remove clear filler words/disfluencies (\"um\", \"uh\", false starts) and fix " +
            "only obvious transcription errors. Keep the speaker's original phrasing and sentence " +
            "structure. Do not rephrase or restructure sentences that are already grammatical.",
        ["standard"] = "Remove filler words/disfluencies (\"um\", \"uh\", filler \"like\", false " +
            "starts, repeated words), then fix grammar, punctuation, and capitalization so it reads naturally.",
        ["aggressive"] = "Do everything in standard cleanup, and additionally restructure sentences " +
            "and tighten wordy phrasing for clarity and concision. Never add information or change " +
            "the speaker's actual meaning. Rule 3 always wins if these ever conflict.",
    };

    public static string Resolve(string level) =>
        Instructions.TryGetValue(level, out string? instruction) ? instruction : Instructions["standard"];
}
