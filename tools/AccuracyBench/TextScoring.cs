using System.Text;

namespace VoiceCtrl.Tools.AccuracyBench;

/// <summary>Word error counts between a script and what a pipeline returned.</summary>
public readonly record struct WordErrors(int Substitutions, int Deletions, int Insertions, int ReferenceWords)
{
    public int Total => Substitutions + Deletions + Insertions;

    public static WordErrors operator +(WordErrors a, WordErrors b) => new(
        a.Substitutions + b.Substitutions,
        a.Deletions + b.Deletions,
        a.Insertions + b.Insertions,
        a.ReferenceWords + b.ReferenceWords);

    public double Rate => ReferenceWords == 0 ? 0 : (double)Total / ReferenceWords;
}

/// <summary>
/// The scoring rules, in one place so a number means the same thing in every run. WER ignores case
/// and punctuation (the way the public leaderboards count it); term recall is reported twice, once
/// the same way and once with case kept, because "kubernetes" for "Kubernetes" is a real miss for
/// someone who dictates names.
/// </summary>
public static class TextScoring
{
    private static readonly string[] SmallNumbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    /// <summary>Words with punctuation turned into spaces, so "scikit-learn" and "scikit learn" agree.
    /// Digits up to twenty are spelled out, because the scripts contain no digits and a recogniser may add them.</summary>
    public static string[] Words(string text, bool keepCase)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '\'' || c == '’')
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? (keepCase ? c : char.ToLowerInvariant(c)) : ' ');
        }

        string[] words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (int.TryParse(words[i], out int number) && number is >= 0 and <= 20)
            {
                words[i] = SmallNumbers[number];
            }
        }

        return words;
    }

    public static WordErrors Compare(string reference, string hypothesis)
    {
        string[] r = Words(reference, keepCase: false);
        string[] h = Words(hypothesis, keepCase: false);

        // Classic word-level edit distance, counting each kind of error along the cheapest path.
        var cost = new (int Total, int Sub, int Del, int Ins)[r.Length + 1, h.Length + 1];
        for (int i = 1; i <= r.Length; i++)
        {
            cost[i, 0] = (i, 0, i, 0);
        }

        for (int j = 1; j <= h.Length; j++)
        {
            cost[0, j] = (j, 0, 0, j);
        }

        for (int i = 1; i <= r.Length; i++)
        {
            for (int j = 1; j <= h.Length; j++)
            {
                var diagonal = cost[i - 1, j - 1];
                if (r[i - 1] != h[j - 1])
                {
                    diagonal = (diagonal.Total + 1, diagonal.Sub + 1, diagonal.Del, diagonal.Ins);
                }

                var deletion = cost[i - 1, j];
                deletion = (deletion.Total + 1, deletion.Sub, deletion.Del + 1, deletion.Ins);
                var insertion = cost[i, j - 1];
                insertion = (insertion.Total + 1, insertion.Sub, insertion.Del, insertion.Ins + 1);

                cost[i, j] = diagonal.Total <= deletion.Total && diagonal.Total <= insertion.Total ? diagonal
                    : deletion.Total <= insertion.Total ? deletion : insertion;
            }
        }

        var end = cost[r.Length, h.Length];
        return new WordErrors(end.Sub, end.Del, end.Ins, r.Length);
    }

    /// <summary>True when the term's words appear one after another in the hypothesis.</summary>
    public static bool TermFound(string term, string hypothesis, bool keepCase)
    {
        string[] needle = Words(term, keepCase);
        string[] haystack = Words(hypothesis, keepCase);
        StringComparison comparison = keepCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (int start = 0; start + needle.Length <= haystack.Length; start++)
        {
            bool match = true;
            for (int k = 0; k < needle.Length && match; k++)
            {
                match = string.Equals(haystack[start + k], needle[k], comparison);
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
