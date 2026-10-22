namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Splits Gemini's answer into the text to paste and, when it edited the transcript, the Original
/// (what was said, before the edit) that the Dictations screen offers back. One call returns both,
/// so Online keeps its single round trip.
/// </summary>
internal static class GeminiOutput
{
    public const string OriginalMarker = "[ORIGINAL_TRANSCRIPT]";

    /// <summary>An Original longer than this many times the text is the model rambling, not a
    /// transcript; the words are not worth keeping and the text is still good.</summary>
    private const int MaxOriginalToTextRatio = 5;

    public static (string Text, string? Original) Split(string answer)
    {
        int at = answer.IndexOf(OriginalMarker, StringComparison.Ordinal);
        if (at < 0)
        {
            return (answer.Trim(), null);
        }

        string text = answer[..at].Trim();
        string original = answer[(at + OriginalMarker.Length)..].Trim();
        if (text.Length == 0)
        {
            // The model put the marker first: what follows is all there is, so never lose it.
            return (original, null);
        }

        return (text, IsWorthKeeping(text, original) ? original : null);
    }

    private static bool IsWorthKeeping(string text, string original)
    {
        List<string> textWords = SmartCleanupGuard.Words(text);
        List<string> originalWords = SmartCleanupGuard.Words(original);

        return originalWords.Count > 0 &&
               originalWords.Count <= (textWords.Count + 10) * MaxOriginalToTextRatio &&
               !originalWords.SequenceEqual(textWords);
    }
}
