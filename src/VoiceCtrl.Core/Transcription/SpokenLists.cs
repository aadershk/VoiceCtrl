using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// A list the speaker spelled out with ordinals ("first ... second ... and third ...") becomes a
/// numbered list, in code (ticket 55). Either engine usually lays it out already, but a small model
/// sometimes keeps the words, writes them one a line, or has its answer refused, and the speaker
/// asked for a list in every app. Only "first" opening a clause (the text's start or after a
/// punctuation mark) and a "second" after it start a list, so "the first and second quarter" or
/// "first of all, the second draft" stay as they are.
/// </summary>
internal static partial class SpokenLists
{
    private const int MaxItemWords = 20;

    private static readonly string[] Ordinals = ["first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth"];

    public static string Format(string text) =>
        string.Join('\n', OrdinalLines(text.Split('\n')).Select(InlineList));

    /// <summary>"First, update the page" / "second, send the report" on lines of their own.</summary>
    private static List<string> OrdinalLines(string[] lines)
    {
        List<string> result = [.. lines];
        for (int start = 0; start < result.Count; start++)
        {
            int count = 0;
            while (start + count < result.Count && count < Ordinals.Length &&
                   OrdinalLine().Match(result[start + count]) is { Success: true } line &&
                   line.Groups["ordinal"].Value.Equals(Ordinals[count], StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }

            if (count < 2)
            {
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                string item = Item(OrdinalLine().Match(result[start + i]).Groups["item"].Value);
                result[start + i] = $"{i + 1}. {item}";
            }

            start += count - 1;
        }

        return result;
    }

    /// <summary>"Two things: first, X, and second, Y. Thanks." in one line.</summary>
    private static string InlineList(string line)
    {
        if (ListItem().IsMatch(line))
        {
            return line;
        }

        List<Match> markers = [];
        foreach (Match marker in OrdinalMarker().Matches(line))
        {
            string expected = Ordinals[markers.Count];
            bool first = markers.Count == 0;
            if (!marker.Groups["ordinal"].Value.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                (first && marker.Groups["joiner"].Success) ||
                (!first && marker.Index < markers[^1].Index + markers[^1].Length))
            {
                continue;
            }

            markers.Add(marker);
            if (markers.Count == Ordinals.Length)
            {
                break;
            }
        }

        if (markers.Count < 2)
        {
            return line;
        }

        // The last item runs to the end of its sentence; whatever follows starts a new line.
        int lastStart = markers[^1].Index + markers[^1].Length;
        Match sentenceEnd = SentenceEnd().Match(line, lastStart);
        int listEnd = sentenceEnd.Success ? sentenceEnd.Index : line.Length;

        List<string> items = [];
        for (int i = 0; i < markers.Count; i++)
        {
            int from = markers[i].Index + markers[i].Length;
            int to = i + 1 < markers.Count ? markers[i + 1].Index : listEnd;
            string item = Item(line[from..to]);
            int words = item.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (words == 0 || words > MaxItemWords)
            {
                return line;
            }

            items.Add($"{i + 1}. {item}");
        }

        string leadIn = line[..markers[0].Index].TrimEnd().TrimEnd(',', ';', ':', '.').TrimEnd();
        string rest = sentenceEnd.Success ? line[(sentenceEnd.Index + sentenceEnd.Length)..].Trim() : string.Empty;
        IEnumerable<string> parts = (leadIn.Length > 0 ? [leadIn + ":"] : Array.Empty<string>())
            .Concat(items)
            .Concat(rest.Length > 0 ? [InlineList(rest)] : Array.Empty<string>());
        return string.Join('\n', parts);
    }

    /// <summary>An item without the commas and "and" that joined it to the next, with a capital.</summary>
    private static string Item(string text)
    {
        string item = TrailingJoiner().Replace(text.Trim(), string.Empty).Trim().TrimEnd('.', ',', ';').Trim();
        return item.Length == 0 ? item : char.ToUpperInvariant(item[0]) + item[1..];
    }

    [GeneratedRegex(@"^\s*(?:\d+[.)]|[-*•])\s")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\s*(?<ordinal>first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth)\b[,:]?\s+(?<item>\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalLine();

    /// <summary>An ordinal opening a clause: at the start, after a punctuation mark, or (for the second
    /// and later) after "and" or "then". "First of all" is a phrase, not a list.</summary>
    [GeneratedRegex(
        @"(?:^|(?<=[,;:.!?])\s*|\s+(?<joiner>and\s+then|and|then)\s+)" +
        @"(?<ordinal>first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth)\b(?!\s+of\b)[,:]?\s+(?=\S)",
        RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalMarker();

    [GeneratedRegex(@"[.!?](?=\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"(?:[,;]?\s*\b(?:and\s+then|and|then)\s*)$|[,;]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingJoiner();
}
