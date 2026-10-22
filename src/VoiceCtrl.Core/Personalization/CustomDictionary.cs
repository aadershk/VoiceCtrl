namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// One Dictionary word, plus what a recogniser tends to hear instead ("blue peak" for
/// "Bluepeak"). <see cref="AlsoHeardAs"/> is empty for a word that only needs its spelling.
/// </summary>
public sealed record DictionaryEntry(string Word, IReadOnlyList<string> AlsoHeardAs);

/// <summary>
/// The speaker's own vocabulary: names, product names, acronyms and jargon that general-purpose
/// speech models have no reason to know. Read from a plain one-word-per-line text file so it can
/// be edited in Notepad, which is the only editor every Windows machine is guaranteed to have.
/// A line may add what the word is also heard as after a bar: <c>Bluepeak | blue peak, blu peak</c>.
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
    /// Longest single term kept, and longest single "also heard as" phrase. A dictionary term is a
    /// name or a short phrase; anything past this is a pasted sentence rather than vocabulary, and
    /// the offline matcher sizes a stack buffer from the term's length, so the bound is
    /// load-bearing rather than merely tidy.
    /// </summary>
    public const int MaxTermLength = 64;

    /// <summary>At most this many "also heard as" phrases per word; they go into every prompt too.</summary>
    public const int MaxAlsoHeardAs = 5;

    /// <summary>Separates a word from what it is also heard as, on one line of the file.</summary>
    public const char AlsoHeardAsSeparator = '|';

    public const string SeedContents = """
        # VoiceCtrl custom dictionary
        # One term per line. Names, acronyms, product names, anything a general speech
        # model would not spell the way you do. Lines starting with # are ignored.
        # To say what it is often heard as instead, add it after a bar:
        #   Bluepeak | blue peak, blu peak
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
    /// entry per line, the inverse of reading a file's header then <see cref="Parse"/>-ing its entries.</summary>
    public static string Format(string header, IReadOnlyList<DictionaryEntry> entries) =>
        header + string.Join('\n', entries.Select(FormatLine)) + (entries.Count > 0 ? "\n" : string.Empty);

    public static IReadOnlyList<DictionaryEntry> Parse(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<DictionaryEntry>();

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf(AlsoHeardAsSeparator);
            string term = (separator < 0 ? line : line[..separator]).Trim();
            if (term.Length == 0 || term.Length > MaxTermLength)
            {
                continue;
            }

            // First spelling wins, so a duplicate that differs only in case cannot make the
            // correction below oscillate between two targets.
            if (seen.Add(term))
            {
                IReadOnlyList<string> alsoHeardAs = separator < 0 ? [] : ParseAlsoHeardAs(term, line[(separator + 1)..]);
                entries.Add(new DictionaryEntry(term, alsoHeardAs));
            }

            if (entries.Count == MaxTerms)
            {
                break;
            }
        }

        return entries;
    }

    /// <summary>
    /// Splits the "also heard as" text (the Hub field, or the part of a line after the bar) on
    /// commas. Blank phrases, the word itself, repeats and anything past the limits are dropped,
    /// the same way <see cref="Parse"/> drops an unusable line rather than failing the file.
    /// </summary>
    public static IReadOnlyList<string> ParseAlsoHeardAs(string word, string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { word };
        var phrases = new List<string>();

        foreach (string part in text.Split(','))
        {
            string phrase = part.Trim();
            if (phrase.Length == 0 || phrase.Length > MaxTermLength || phrase.Contains(AlsoHeardAsSeparator) || !seen.Add(phrase))
            {
                continue;
            }

            phrases.Add(phrase);
            if (phrases.Count == MaxAlsoHeardAs)
            {
                break;
            }
        }

        return phrases;
    }

    private static string FormatLine(DictionaryEntry entry) =>
        entry.AlsoHeardAs.Count == 0
            ? entry.Word
            : $"{entry.Word} {AlsoHeardAsSeparator} {string.Join(", ", entry.AlsoHeardAs)}";
}
