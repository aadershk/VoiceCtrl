namespace VoiceCtrl.Core.Transcription;

internal static class CleanupLevelMapper
{
    private static readonly Dictionary<string, string> Instructions = new(StringComparer.OrdinalIgnoreCase)
    {
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
