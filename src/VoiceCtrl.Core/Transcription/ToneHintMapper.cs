namespace VoiceCtrl.Core.Transcription;

internal static class ToneHintMapper
{
    private static readonly Dictionary<string, string> ProcessNameToHint = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slack"] = "casual, conversational tone; contractions are fine",
        ["discord"] = "casual, conversational tone; contractions are fine",
        ["outlook"] = "professional, formal tone; no contractions, complete sentences",
        ["winword"] = "professional, formal tone; no contractions, complete sentences",
        ["code"] = "likely a code comment or commit message; preserve technical terms and casing exactly as spoken",
        ["devenv"] = "likely a code comment or commit message; preserve technical terms and casing exactly as spoken",
        ["windowsterminal"] = "likely a shell command; preserve technical terms/casing exactly, no trailing punctuation",
    };

    public static string? Resolve(string? processName) =>
        processName is not null && ProcessNameToHint.TryGetValue(processName, out string? hint) ? hint : null;
}
