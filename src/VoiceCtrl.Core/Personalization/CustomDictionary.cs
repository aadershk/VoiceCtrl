namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// The speaker's own vocabulary: names, product names, acronyms and jargon that general-purpose
/// speech models have no reason to know. Read from a plain one-term-per-line text file so it can
/// be edited in Notepad, which is the only editor every Windows machine is guaranteed to have.
/// </summary>
public static class CustomDictionary
{
    /// <summary>
    /// Hard cap on how many terms are kept. Online sends these in the prompt, where an unbounded
    /// list would grow the request on every single dictation, and offline scans each term against
    /// the transcript, where the cost is linear in the count. Two hundred is far more proper nouns
    /// than one person uses and keeps both paths' worst case predictable.
    /// </summary>
    public const int MaxTerms = 200;

    /// <summary>
    /// Longest single term kept. A dictionary term is a name or a short phrase; anything past this
    /// is a pasted sentence rather than vocabulary, and the offline matcher sizes a stack buffer
    /// from the term's length, so the bound is load-bearing rather than merely tidy.
    /// </summary>
    public const int MaxTermLength = 64;

    public const string SeedContents = """
        # VoiceCtrl custom dictionary
        # One term per line. Names, acronyms, product names, anything a general speech
        # model would not spell the way you do. Lines starting with # are ignored.
        #
        # Online mode passes these to the model as a spelling reference.
        # Offline mode corrects near-misses in the transcript to match them.
        #
        # Examples, delete these:
        # Kubernetes
        # PostgreSQL
        """;

    /// <summary>
    /// The file's leading run of comment/blank lines, verbatim, terminated by a trailing newline
    /// (empty string if the file has none). A GUI editor that rewrites the file via <see cref="Format"/>
    /// carries this forward so a user's own header commentary, or the seed file's explanatory
    /// comments, survives edits made through the Hub rather than only through Notepad.
    /// </summary>
    public static string ExtractHeader(string contents)
    {
        var headerLines = new List<string>();

        foreach (string rawLine in contents.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                headerLines.Add(line);
                continue;
            }

            break;
        }

        return headerLines.Count == 0 ? string.Empty : string.Join('\n', headerLines) + "\n";
    }

    /// <summary>Renders <paramref name="header"/> (from <see cref="ExtractHeader"/>) followed by one
    /// term per line, the inverse of reading a file's header then <see cref="Parse"/>-ing its terms.</summary>
    public static string Format(string header, IReadOnlyList<string> terms) =>
        header + string.Join('\n', terms) + (terms.Count > 0 ? "\n" : string.Empty);

    public static IReadOnlyList<string> Parse(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var terms = new List<string>();

        foreach (string rawLine in lines)
        {
            string term = rawLine.Trim();
            if (term.Length == 0 || term.Length > MaxTermLength || term.StartsWith('#'))
            {
                continue;
            }

            // First spelling wins, so a duplicate that differs only in case cannot make the
            // correction below oscillate between two targets.
            if (seen.Add(term))
            {
                terms.Add(term);
            }

            if (terms.Count == MaxTerms)
            {
                break;
            }
        }

        return terms;
    }
}
