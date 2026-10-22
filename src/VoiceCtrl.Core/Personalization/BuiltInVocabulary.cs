using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// The spellings the app knows before the user has typed a single Dictionary entry: a large list
/// of names, places, brands and jargon, and a list of ordinary English words. The user never sees
/// either. They give the offline path the same two things the Dictionary gives it (a rewrite of a
/// near-miss, and a spelling reference for the local model), for words the user never thought of.
///
/// A list this size makes false rewrites the main risk, so a word is only ever replaced when the
/// speech model plainly did not know it: the word is neither ordinary English nor itself a listed
/// term, and it sounds like exactly one listed spelling. That is the text-only stand-in for "the
/// recogniser was unsure", since the recogniser does not report a confidence per word.
/// </summary>
public sealed partial class BuiltInVocabulary
{
    /// <summary>The longest run of words that may be joined into one term ("kuber netties").</summary>
    private const int MaxWindowWords = 3;

    /// <summary>Below this a sound key is too common to say anything: it matches a large share of
    /// the list.</summary>
    private const int MinKeyLength = 4;

    /// <summary>Extra letters of slack when the candidate goes to the model rather than straight
    /// into the text.</summary>
    private const int LooseExtraLetters = 2;

    /// <summary>
    /// The share of the list, from the top, that <see cref="Apply"/> may rewrite onto by sound. The
    /// rarer a name, the less likely the speaker said it and the more likely it is a lookalike of a
    /// name the speech model got right but the list does not hold (measured: one-letter swaps onto
    /// the obscure tail of a 180k list damaged more names than they fixed). The model's spelling
    /// reference (<see cref="Suggest"/>) is not limited, since it can weigh the whole sentence.
    /// </summary>
    public static double RewriteShare { get; set; } = 0.10;

    /// <summary>A list shorter than this is all popular terms; the share only trims a long tail.</summary>
    private const int AlwaysPopular = 1000;

    public static BuiltInVocabulary Empty { get; } = new([], []);

    private static readonly Lazy<BuiltInVocabulary> SharedInstance = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The list that ships inside the app. Building the sound index takes a moment, so the
    /// host calls <see cref="Warm"/> at start and the first Dictation never waits for it.</summary>
    public static BuiltInVocabulary Shared => SharedInstance.Value;

    public static Task Warm() => Task.Run(() => SharedInstance.Value);

    /// <summary>Terms in priority order: when two spellings are equally close, the earlier one wins.</summary>
    private readonly Dictionary<string, List<string>> _termsByKey = new(StringComparer.Ordinal);

    /// <summary>Position in the list: the lower, the more likely a speaker said it.</summary>
    private readonly Dictionary<string, int> _rankByTerm = new(StringComparer.Ordinal);

    /// <summary>Ordinary English plus every listed term, lower-cased: the words the recogniser
    /// can be assumed to have heard right.</summary>
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);

    private readonly HashSet<string> _everyday = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _termByLowercase = new(StringComparer.Ordinal);

    /// <summary>Every listed spelling, most useful first.</summary>
    public IEnumerable<string> Terms => _termByLowercase.Values;

    public BuiltInVocabulary(IEnumerable<string> terms, IEnumerable<string> everydayWords)
    {
        foreach (string word in everydayWords)
        {
            string lower = word.ToLowerInvariant();
            if (word.Length > 0 && char.IsLower(word[0]))
            {
                _everyday.Add(lower);
            }

            // Capitalised entries (Thursday, Amsterdam, Jensen) are words the recogniser knows too.
            _known.Add(lower);
        }

        foreach (string term in terms)
        {
            string trimmed = term.Trim();
            if (trimmed.Length == 0 || trimmed.Length > CustomDictionary.MaxTermLength)
            {
                continue;
            }

            string lower = trimmed.ToLowerInvariant();
            _known.Add(lower);
            _termByLowercase.TryAdd(lower, trimmed);

            string key = Key(trimmed);
            if (key.Length < MinKeyLength)
            {
                continue;
            }

            if (!_termsByKey.TryGetValue(key, out List<string>? bucket))
            {
                _termsByKey[key] = bucket = [];
            }

            bucket.Add(trimmed);
            _rankByTerm.TryAdd(trimmed, _rankByTerm.Count);
        }
    }

    private static BuiltInVocabulary LoadEmbedded()
    {
        Assembly assembly = typeof(BuiltInVocabulary).Assembly;
        IReadOnlyList<string>? terms = ReadLines(assembly, "VoiceCtrl.Vocabulary.terms.txt.gz");
        IReadOnlyList<string>? everyday = ReadLines(assembly, "VoiceCtrl.Vocabulary.everyday-words.txt.gz");
        return terms is null || everyday is null ? Empty : new BuiltInVocabulary(terms, everyday);
    }

    /// <summary>One entry per line, gzip-compressed; null when the resource is not in this build.</summary>
    private static IReadOnlyList<string>? ReadLines(Assembly assembly, string resource)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return null;
        }

        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public int TermCount => _termByLowercase.Count;

    /// <summary>True for an ordinary English word, as opposed to a name or term that is only listed.
    /// Smart cleanup may mend a word into one of these, never into a rare spelling.</summary>
    public bool IsOrdinaryWord(string word) => _everyday.Contains(word.ToLowerInvariant());

    /// <summary>True for a word a speaker plausibly said as written: ordinary English, or a listed
    /// term among the popular head of the list. A word outside both is one the recogniser probably
    /// garbled, whether the list holds it as an obscure term or not at all.</summary>
    public bool IsEstablishedWord(string word)
    {
        string lower = word.ToLowerInvariant();
        if (_everyday.Contains(lower))
        {
            return true;
        }

        return _termByLowercase.TryGetValue(lower, out string? term) &&
               (!_rankByTerm.TryGetValue(term, out int rank) || rank < Math.Max(AlwaysPopular, _rankByTerm.Count * RewriteShare));
    }

    /// <summary>True for any word the list holds, ordinary or a name or term.</summary>
    public bool IsKnownWord(string word) => _known.Contains(word.ToLowerInvariant());

    /// <summary>The listed spelling of a name or term with its capitals ("eindhoven" gives
    /// "Eindhoven"); an ordinary or unlisted word comes back lower-cased.</summary>
    public string SpellingOf(string word)
    {
        string lower = word.ToLowerInvariant();
        return !_everyday.Contains(lower) && _termByLowercase.TryGetValue(lower, out string? term) ? term : lower;
    }

    /// <summary>
    /// Replaces each span the speech model most likely misheard with the listed spelling it sounds
    /// like. <paramref name="personalTerms"/> are the user's own Dictionary words: they are left
    /// exactly as they are, since the user's spelling outranks the list.
    /// </summary>
    /// <param name="changes">Receives each rewrite as the words heard and the listed spelling, for the
    /// engine record.</param>
    public string Apply(string text, IReadOnlyCollection<string>? personalTerms = null, List<(string Heard, string Term)>? changes = null)
    {
        if (_termsByKey.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var edits = new List<(int Start, int Length, string Replacement)>();
        foreach (Candidate candidate in Candidates(text, personalTerms, loose: false))
        {
            edits.Add((candidate.Start, candidate.Length, candidate.Term));
            string heard = text.Substring(candidate.Start, candidate.Length);
            if (!string.Equals(heard, candidate.Term, StringComparison.Ordinal))
            {
                changes?.Add((heard, candidate.Term));
            }
        }

        if (edits.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        foreach ((int start, int length, string replacement) in edits.OrderByDescending(edit => edit.Start))
        {
            builder.Remove(start, length);
            builder.Insert(start, replacement);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Spelling references for the local model: for each stretch of <paramref name="text"/> the
    /// speech model probably did not know, the listed spellings that sound like it, at most
    /// <paramref name="max"/> in all, closest first. Each comes with the words as heard, so the
    /// prompt can say "Schiphol (often heard as: Skippel)" and the guard can tell the swap is
    /// anchored to something the speaker said. It is retrieval, not the list: a few terms per
    /// dictation keeps the prompt short and the model away from names nobody said. The net is wider
    /// than <see cref="Apply"/>'s, because the model, unlike the rules, can weigh the sentence.
    /// </summary>
    public IReadOnlyList<DictionaryEntry> Suggest(string text, int max, IReadOnlyCollection<string>? personalTerms = null)
    {
        if (_termsByKey.Count == 0 || string.IsNullOrEmpty(text) || max <= 0)
        {
            return [];
        }

        return Candidates(text, personalTerms, loose: true)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => new DictionaryEntry(candidate.Term, [text.Substring(candidate.Start, candidate.Length)]))
            .DistinctBy(entry => entry.Word, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    private IEnumerable<Candidate> Candidates(string text, IReadOnlyCollection<string>? personalTerms, bool loose)
    {
        MatchCollection words = WordPattern().Matches(text);
        if (words.Count == 0)
        {
            yield break;
        }

        HashSet<string>? personal = personalTerms is { Count: > 0 }
            ? personalTerms.Select(term => term.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal)
            : null;

        bool[] claimed = new bool[words.Count];

        // Longest window first, so "kuber netties" is joined before "netties" can be judged alone.
        for (int size = Math.Min(MaxWindowWords, words.Count); size >= 1; size--)
        {
            for (int index = 0; index + size <= words.Count; index++)
            {
                if (IsAnyClaimed(claimed, index, size))
                {
                    continue;
                }

                int start = words[index].Index;
                Match last = words[index + size - 1];
                int length = last.Index + last.Length - start;
                string spoken = text.Substring(start, length);

                if (!TryFind(spoken, size, personal, loose, out string? term, out int distance))
                {
                    continue;
                }

                for (int offset = 0; offset < size; offset++)
                {
                    claimed[index + offset] = true;
                }

                yield return new Candidate(start, length, term, distance);
            }
        }
    }

    private bool TryFind(string spoken, int wordCount, HashSet<string>? personal, bool loose, out string term, out int distance)
    {
        term = string.Empty;
        distance = 0;

        string lower = spoken.ToLowerInvariant();
        if (personal is not null && personal.Contains(lower))
        {
            return false;
        }

        MatchCollection parts = WordPattern().Matches(lower);
        string glued = string.Concat(parts.Select(part => part.Value));

        // Words the recogniser split or cased differently from a listed term, but letter for letter
        // the same ("light GBM" for LightGBM, "stats models" for statsmodels, "jupyterlab"). Nothing
        // is guessed, so it holds even when the pieces are ordinary words, unless the joined word is
        // itself ordinary English ("any one" is not "Anyone").
        // Short function words glue into names too ("and a" is "Anda"), so no part may be one.
        bool plausibleJoin = glued.Length >= 6 && parts.All(part => part.Length >= 3 && !DictionaryCorrector.CommonWords.Contains(part.Value));
        if ((wordCount == 1 || plausibleJoin) && _termByLowercase.TryGetValue(glued, out string? exact) && !_everyday.Contains(glued) &&
            !string.Equals(exact, spoken, StringComparison.Ordinal) &&
            (wordCount > 1 || HasInnerCapital(exact) || IsAllLowercase(spoken)))
        {
            term = exact;
            return true;
        }

        // Every word of the span has to be one the recogniser could not have known: an ordinary
        // word, or a listed name, is taken as heard correctly.
        if (parts.Any(part => _known.Contains(StripPossessive(part.Value))))
        {
            return false;
        }

        string key = Key(spoken);
        if (key.Length < MinKeyLength || !_termsByKey.TryGetValue(key, out List<string>? bucket))
        {
            return false;
        }

        int best = int.MaxValue;
        string? chosen = null;
        foreach (string candidate in bucket)
        {
            if (!loose && _rankByTerm.TryGetValue(candidate, out int rank) && rank >= Math.Max(AlwaysPopular, _rankByTerm.Count * RewriteShare))
            {
                continue;
            }

            string candidateLetters = string.Concat(WordPattern().Matches(candidate.ToLowerInvariant()).Select(m => m.Value));
            if (!SameOpening(glued, candidateLetters))
            {
                continue;
            }

            int budget = LetterBudget(candidateLetters.Length) + (loose ? LooseExtraLetters : 0);
            if (Math.Abs(candidateLetters.Length - glued.Length) > budget)
            {
                continue;
            }

            int letters = LetterDistance(glued, candidateLetters, budget);
            if (letters <= budget && letters < best)
            {
                best = letters;
                chosen = candidate;
            }
        }

        if (chosen is null)
        {
            return false;
        }

        term = chosen;
        distance = best;
        return true;
    }

    /// <summary>
    /// The sound key with p and f treated as one sound as well. Recognisers swap them on unfamiliar
    /// words, and spelling hides it: "ph" in Schiphol is a p and an h, not an f. The letter distance
    /// afterwards keeps the looser key honest.
    /// </summary>
    private static string Key(string text)
    {
        var key = new StringBuilder();
        foreach (char sound in PhoneticKey.Encode(text))
        {
            char folded = sound == 'F' ? 'P' : sound;
            if (key.Length == 0 || key[^1] != folded)
            {
                key.Append(folded);
            }
        }

        return key.ToString();
    }

    /// <summary>One slip in an ordinary-length name, since two turn a correct "Grafana" into "Gravina"
    /// or "polars" into "Pillars": the speech model gives no per-word confidence, so the rules can
    /// only act where a wrong guess is very unlikely. More slips only in a long name, where a sound
    /// match plus a few slips is still one name. The model gets a wider net through <see cref="Suggest"/>.</summary>
    private static int LetterBudget(int termLetters) => termLetters switch
    {
        <= 9 => 1,
        <= 13 => 2,
        _ => 3,
    };

    /// <summary>A recogniser seldom gets the first sound wrong and the rest right, so a match that
    /// does not open alike is more likely a coincidence than a mishearing.</summary>
    private static bool SameOpening(string heard, string term) =>
        heard.Length > 0 && term.Length > 0 && (heard[0] == term[0] || (IsVowel(heard[0]) && IsVowel(term[0])));

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static string StripPossessive(string word) =>
        word.EndsWith("'s", StringComparison.Ordinal) || word.EndsWith("’s", StringComparison.Ordinal) ? word[..^2] : word;

    private static bool HasInnerCapital(string term) => term.Skip(1).Any(char.IsUpper);

    private static bool IsAllLowercase(string word) => word.All(c => !char.IsLetter(c) || char.IsLower(c));

    private static bool IsAnyClaimed(bool[] claimed, int index, int count)
    {
        for (int offset = 0; offset < count; offset++)
        {
            if (claimed[index + offset])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Plain edit distance between two short strings, abandoned once it passes <paramref name="budget"/>.</summary>
    private static int LetterDistance(string a, string b, int budget)
    {
        Span<int> previous = stackalloc int[b.Length + 1];
        Span<int> current = stackalloc int[b.Length + 1];
        for (int column = 0; column <= b.Length; column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= a.Length; row++)
        {
            current[0] = row;
            int rowMinimum = row;
            for (int column = 1; column <= b.Length; column++)
            {
                int substitution = a[row - 1] == b[column - 1] ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + substitution);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > budget)
            {
                return budget + 1;
            }

            current.CopyTo(previous);
        }

        return previous[b.Length];
    }

    private readonly record struct Candidate(int Start, int Length, string Term, int Distance);

    [GeneratedRegex(@"\p{L}[\p{L}\p{Nd}'’]*")]
    private static partial Regex WordPattern();
}
