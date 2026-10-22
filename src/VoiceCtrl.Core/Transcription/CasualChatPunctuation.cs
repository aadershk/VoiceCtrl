namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// People do not end a casual message's line with a full stop, and Wispr Flow drops it (research 31).
/// The Casual and Very casual Styles (ticket 56) take it off every line. Done in code on the finished
/// text rather than asked of either engine, so Online and Offline agree and the speaker's words are
/// never touched.
/// </summary>
internal static class CasualChatPunctuation
{
    /// <summary>Words whose full stop is part of the word: "etc." keeps it at a line's end.</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "etc.", "e.g.", "i.e.", "a.m.", "p.m.", "vs.", "mr.", "mrs.", "ms.", "dr.", "st.", "jr.", "sr.",
    };

    public static string Apply(string text) =>
        string.Join('\n', text.Split('\n').Select(DropClosingFullStop));

    /// <summary>Whether <paramref name="word"/>, full stop included, is one whose stop is part of it.</summary>
    public static bool IsAbbreviation(string word) => Abbreviations.Contains(word.TrimStart('(', '"', '\''));

    private static string DropClosingFullStop(string line)
    {
        string trimmed = line.TrimEnd();
        if (trimmed.Length < 2 || trimmed[^1] != '.' || trimmed[^2] == '.')
        {
            return line;
        }

        string lastWord = trimmed[(trimmed.LastIndexOf(' ') + 1)..];
        return IsAbbreviation(lastWord) ? line : trimmed[..^1];
    }
}
