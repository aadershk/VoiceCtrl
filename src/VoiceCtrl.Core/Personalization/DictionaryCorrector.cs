using System.Text;
using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// Snaps near-misses in an offline transcript onto the speaker's own vocabulary. Online mode does
/// not need this: the dictionary goes into the prompt and the model applies it while it still has
/// the audio to check against. The local model takes no instructions at all, so the only place
/// left to apply the terms is the text it has already produced.
///
/// Working from text alone means every correction is a guess, so the guesses are kept deliberately
/// timid. A miss costs the user one re-spoken word. A false correction silently rewrites a word
/// they did say, which they may not notice until it matters, and that asymmetry is what sets every
/// threshold below.
/// </summary>
public static partial class DictionaryCorrector
{
    /// <summary>
    /// Words common enough that a near-miss against a dictionary term is far more likely to be the
    /// speaker using ordinary English than a misheard proper noun. Without this, a two-letter
    /// difference is enough to turn "for" into a term like "Ford" on every dictation.
    /// </summary>
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "any", "can", "had", "her", "was",
        "one", "our", "out", "day", "get", "has", "him", "his", "how", "its", "may", "new", "now",
        "old", "see", "two", "way", "who", "boy", "did", "man", "men", "put", "say", "she", "too",
        "use", "that", "with", "have", "this", "will", "your", "from", "they", "know", "want",
        "been", "good", "much", "some", "time", "very", "when", "come", "here", "just", "like",
        "long", "make", "many", "over", "such", "take", "than", "them", "well", "were", "what",
        "work", "back", "call", "each", "even", "find", "give", "hand", "high", "keep", "kind",
        "last", "left", "life", "live", "look", "made", "most", "move", "must", "name", "need",
        "next", "only", "open", "part", "play", "said", "same", "seem", "show", "side", "tell",
        "then", "thing", "think", "three", "under", "water", "where", "which", "while", "would",
        "about", "after", "again", "could", "every", "first", "found", "great", "house", "large",
        "learn", "never", "other", "place", "point", "right", "small", "sound", "still", "study",
        "their", "there", "these", "those", "thought", "through", "before", "because", "should",
    };

    /// <summary>
    /// Applies the dictionary to <paramref name="text"/>. Terms are matched on whole words only,
    /// and each span of the transcript can be claimed once, so two similar terms cannot fight over
    /// the same words.
    /// </summary>
    public static string Apply(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        MatchCollection words = WordPattern().Matches(text);
        if (words.Count == 0)
        {
            return text;
        }

        bool[] claimed = new bool[words.Count];
        var edits = new List<(int Start, int Length, string Replacement)>();

        // Longest first, measured in words then characters, so "Amsterdam Schiphol" gets the chance
        // to claim both words before a single-word "Schiphol" takes one of them.
        foreach (string term in terms
            .OrderByDescending(CountWords)
            .ThenByDescending(term => term.Length))
        {
            int termWordCount = CountWords(term);

            for (int index = 0; index + termWordCount <= words.Count; index++)
            {
                if (IsAnyClaimed(claimed, index, termWordCount))
                {
                    continue;
                }

                int start = words[index].Index;
                Match lastWord = words[index + termWordCount - 1];
                int length = lastWord.Index + lastWord.Length - start;

                if (!ShouldCorrect(text.AsSpan(start, length), term))
                {
                    continue;
                }

                for (int offset = 0; offset < termWordCount; offset++)
                {
                    claimed[index + offset] = true;
                }

                edits.Add((start, length, term));
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

    private static bool ShouldCorrect(ReadOnlySpan<char> candidate, string term)
    {
        if (candidate.SequenceEqual(term))
        {
            return false;
        }

        // Same word, wrong case. Always safe, and the single most common thing the dictionary is
        // there to fix, so it bypasses every guard below.
        if (candidate.Equals(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The distance matrix below is stack-allocated, so an unbounded term length would be a
        // stack overflow rather than a slow correction. Parse already caps this; the guard is here
        // because the method is public and must be safe against any list it is handed.
        if (term.Length > CustomDictionary.MaxTermLength)
        {
            return false;
        }

        int budget = EditBudget(term.Length);
        if (budget == 0 || Math.Abs(candidate.Length - term.Length) > budget)
        {
            return false;
        }

        // Checked after the exact-match branch above, so a dictionary term that genuinely is a
        // common word still gets its capitalisation fixed; only fuzzy rewriting is blocked.
        if (CommonWords.Contains(candidate.ToString()))
        {
            return false;
        }

        return DistanceWithin(candidate, term, budget);
    }

    /// <summary>
    /// Roughly a quarter of the term's length, in bands. Short terms get no fuzzy budget at all:
    /// at four characters a single edit already reaches a large share of the English lexicon, so
    /// there is no threshold that catches real mishearings without also catching real words.
    /// </summary>
    private static int EditBudget(int termLength) => termLength switch
    {
        < 5 => 0,
        < 9 => 1,
        < 13 => 2,
        _ => 3,
    };

    /// <summary>
    /// Levenshtein distance, answered only as "within budget or not". Abandoning a row the moment
    /// every cell in it exceeds the budget is what keeps this affordable across 200 terms on every
    /// transcription, since almost every pair fails within the first row or two.
    /// </summary>
    private static bool DistanceWithin(ReadOnlySpan<char> candidate, string term, int budget)
    {
        int candidateLength = candidate.Length;
        int termLength = term.Length;

        Span<int> previous = stackalloc int[termLength + 1];
        Span<int> current = stackalloc int[termLength + 1];

        for (int column = 0; column <= termLength; column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= candidateLength; row++)
        {
            current[0] = row;
            int rowMinimum = current[0];
            char candidateChar = char.ToLowerInvariant(candidate[row - 1]);

            for (int column = 1; column <= termLength; column++)
            {
                int substitutionCost = candidateChar == char.ToLowerInvariant(term[column - 1]) ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > budget)
            {
                return false;
            }

            current.CopyTo(previous);
        }

        return previous[termLength] <= budget;
    }

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

    private static int CountWords(string term) => WordPattern().Matches(term).Count;

    /// <summary>Letters, plus digits and apostrophes once a word is already under way. Terms built
    /// from symbols ("C#", ".NET") are outside what this can match on word boundaries; those are
    /// left to Online mode, where the model sees them as a spelling reference instead.</summary>
    [GeneratedRegex(@"\p{L}[\p{L}\p{Nd}'’]*")]
    private static partial Regex WordPattern();
}
