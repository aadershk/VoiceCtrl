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
