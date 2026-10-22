using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// "Two changes, first rename the helper, second add a test" is a lead-in plus a list, but the local
/// model often writes only the list, or shortens the lead-in ("Can you do three things for me. First
/// ..." comes back as "Three things for me:"), and the guard then refuses it for dropping words. The
/// lead-in is the speaker's own words before "first" (the sentence holding them, when it ends before
/// "first"), so it is put back in code rather than asking the model again. Only a model answer that is
/// already a list gets one.
/// </summary>
internal static partial class ListLeadIn
{
    private const int MaxLeadInWords = 12;

    public static string Restore(string input, string answer)
    {
        Match firstItem = ListLinePattern().Match(answer);
        if (!firstItem.Success)
        {
            return answer;
        }

        string? leadIn = LeadInOf(input);
        if (leadIn is null)
        {
            return answer;
        }

        string[] leadWords = SmartCleanupGuard.Words(leadIn).ToArray();
        string beforeList = answer[..firstItem.Index].TrimEnd('\n');
        int lineStart = beforeList.LastIndexOf('\n') + 1;
        string lineAbove = beforeList[lineStart..];

        if (lineAbove.Length == 0)
        {
            // Already there (or said in other words) higher up: leave the model's answer for the guard to judge.
            return SmartCleanupGuard.Words(beforeList).Contains(leadWords[0]) ? answer : $"{leadIn}:\n{answer}";
        }

        // The line above the list ends in the model's version of the lead-in sentence. If that sentence
        // is only the lead-in with words left out, the speaker's own goes back in its place.
        int sentenceStart = LastSentenceStart().Match(lineAbove) is { Success: true } boundary ? boundary.Index : 0;
        string[] modelWords = SmartCleanupGuard.Words(lineAbove[sentenceStart..]).ToArray();
        bool shortened = modelWords.Length > 0 && modelWords.Length < leadWords.Length && modelWords.All(leadWords.Contains);

        return shortened
            ? answer[..(lineStart + sentenceStart)] + leadIn + ":" + answer[(lineStart + lineAbove.Length)..]
            : answer;
    }

    /// <summary>The speaker's words before "first", from the start of their sentence, or null when
    /// there are none or too many to be a lead-in.</summary>
    private static string? LeadInOf(string input)
    {
        Match match = LeadInPattern().Match(input);
        if (!match.Success)
        {
            return null;
        }

        string before = match.Groups["lead"].Value.Trim().TrimEnd(',', ':', ';', '.').Trim();
        string leadIn = SentenceEnd().Split(before)[^1].Trim();
        int words = leadIn.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return words == 0 || words > MaxLeadInWords || !SmartCleanupGuard.Words(leadIn).Any() ? null : leadIn;
    }

    [GeneratedRegex(@"^\s*(?:\d+\.|-)\s", RegexOptions.Multiline)]
    private static partial Regex ListLinePattern();

    /// <summary>Words, then "first" (not the very first word), then the first item.</summary>
    [GeneratedRegex(@"^(?<lead>[^\n]+?)[,:.]?\s+first\b[,:]?\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex LeadInPattern();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"(?<=[.!?]\s+)(?=[^.!?]+$)")]
    private static partial Regex LastSentenceStart();
}
