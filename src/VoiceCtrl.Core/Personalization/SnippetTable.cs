using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// Spoken shorthand expanded after transcription: say "my work email", get the address. Applied to
/// both transcription paths, because it is a text substitution the speaker asked for rather than
/// anything the recogniser could have got right or wrong.
///
/// Expansion is a single left-to-right pass over the transcript, so an expansion that happens to
/// contain another trigger is never rescanned. That is what makes a pair of snippets that refer to
/// each other terminate instead of looping.
/// </summary>
public sealed class SnippetTable
{
    /// <summary>Bounds the size of the combined pattern built below, which is one alternation over
    /// every trigger.</summary>
    public const int MaxSnippets = 200;

    public const string SeedContents = """
        # VoiceCtrl snippets
        # One per line, as: trigger = expansion
        # Say the trigger, get the expansion. Matching ignores case and only fires on whole
        # words, so a trigger never fires inside a longer word.
        # Use \n in the expansion for a line break. Lines starting with # are ignored.
        #
        # Examples, delete these:
        # my work email = firstname.lastname@example.com
        # standard signoff = Kind regards,\nFirstname
        """;

    public static SnippetTable Empty { get; } = new([]);

    private readonly Dictionary<string, string> _expansionsByTrigger;
    private readonly Regex? _triggerPattern;

    public IReadOnlyList<KeyValuePair<string, string>> Snippets { get; }

    private SnippetTable(IReadOnlyList<KeyValuePair<string, string>> snippets)
    {
        Snippets = snippets;
        _expansionsByTrigger = snippets.ToDictionary(
            snippet => snippet.Key, snippet => snippet.Value, StringComparer.OrdinalIgnoreCase);

        _triggerPattern = snippets.Count == 0 ? null : BuildTriggerPattern(snippets);
    }

    public static SnippetTable Parse(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snippets = new List<KeyValuePair<string, string>>();

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string trigger = line[..separatorIndex].Trim();
            string expansion = line[(separatorIndex + 1)..].Trim().Replace("\\n", "\n");

            // An empty expansion is allowed: deleting a spoken filler phrase outright is a
            // legitimate use. An empty trigger is not, since it would match everywhere.
            if (trigger.Length == 0 || !seen.Add(trigger))
            {
                continue;
            }

            snippets.Add(new KeyValuePair<string, string>(trigger, expansion));

            if (snippets.Count == MaxSnippets)
            {
                break;
            }
        }

        return new SnippetTable(snippets);
    }

    public string Expand(string text)
    {
        if (_triggerPattern is null || string.IsNullOrEmpty(text))
        {
            return text;
        }

        return _triggerPattern.Replace(
            text,
            match => _expansionsByTrigger.TryGetValue(match.Value, out string? expansion) ? expansion : match.Value);
    }

    /// <summary>
    /// One alternation, longest trigger first. .NET alternation is leftmost-first rather than
    /// leftmost-longest, so ordering the branches by descending length is what makes "my work
    /// email" win over "my work" at the same position instead of losing to whichever was listed
    /// first in the file.
    /// </summary>
    private static Regex BuildTriggerPattern(IReadOnlyList<KeyValuePair<string, string>> snippets)
    {
        IEnumerable<string> alternatives = snippets
            .Select(snippet => snippet.Key)
            .OrderByDescending(trigger => trigger.Length)
            .Select(Regex.Escape);

        // \b would be wrong here: a trigger is free to start or end with punctuation, and \b's
        // meaning flips depending on the character next to it. Explicit "not adjacent to a word
        // character" lookarounds behave the same way whatever the trigger looks like.
        string pattern = $@"(?<![\p{{L}}\p{{Nd}}])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{Nd}}])";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
