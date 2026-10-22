using System.Text;
using System.Text.RegularExpressions;
using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>Whether Smart cleanup's text may replace the rule-cleaned text, and if not, why.</summary>
public readonly record struct GuardResult(bool Accepted, string? Reason)
{
    public static GuardResult Accept { get; } = new(true, null);

    public static GuardResult Reject(string reason) => new(false, reason);
}

/// <summary>
/// The word-level check between the local language model and the user's text (research 33 §5).
/// A small model is a guarded editor, not an author: it may drop fillers and repeats, follow a
/// retraction, add punctuation, numbering and layout, respell a Dictionary term and mend a word the
/// recogniser garbled, but it must not add content or answer what was dictated. The guard compares
/// the model's output with the words the recogniser heard and accepts or rejects the whole text; on
/// a rejection the caller keeps the rule-cleaned text, which is always available.
/// </summary>
internal static partial class SmartCleanupGuard
{
    /// <summary>The longest span a retraction may remove ("send it to John actually no make that").</summary>
    private const int MaxRetractedRun = 12;

    /// <summary>How far before a retraction marker the words it takes back may start.</summary>
    private const int LeadInWords = 8;

    /// <summary>Insertions the light and standard levels tolerate, all of them function words.</summary>
    private const int MaxInsertions = 2;

    /// <summary>Words the model may mend, each within <see cref="MaxRepairDistance"/> letters of what
    /// the recogniser wrote ("refuctation" to "refutation").</summary>
    private const int MaxRepairs = 3;

    private const int MaxRepairDistance = 2;
    private const int MinRepairLength = 5;

    private const double AggressiveMinRatio = 0.5;
    private const double AggressiveMaxRatio = 1.2;

    private static readonly HashSet<string> FunctionWords = ["a", "an", "the", "to", "of", "is", "are"];

    private static readonly HashSet<string> Fillers =
        ["um", "uh", "erm", "er", "ah", "hmm", "like", "you", "know", "i", "mean", "so", "well", "basically", "actually", "just", "kind", "sort",
         "okay", "ok", "alright"];

    private static readonly HashSet<string> Conjunctions = ["and", "or"];

    private static readonly HashSet<string> LayoutWords =
        ["bullet", "point", "points", "new", "paragraph", "line", "numbered", "list", "number", "next", "item", "heading", "title"];

    private static readonly HashSet<string> NumberWords =
    [
        "zero", "oh", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
        "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety", "hundred", "thousand",
        "million", "billion", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth",
        "ninth", "tenth", "percent", "dollar", "dollars", "point", "half", "quarter", "a.m", "am", "pm",
        "and", "euro", "euros", "pound", "pounds", "cents", "oclock",
    ];

    private static readonly string[] RetractionMarkers =
        ["actually", "no wait", "wait no", "wait", "no sorry", "scratch that", "make that", "i mean", "sorry", "rather", "correction"];

    public static GuardResult Check(string raw, string output, string cleanupLevel, IReadOnlyCollection<string> dictionaryTerms) =>
        Check(raw, output, cleanupLevel, dictionaryTerms.Select(term => new DictionaryEntry(term, [])).ToList());

    public static GuardResult Check(string raw, string output, string cleanupLevel, IReadOnlyList<DictionaryEntry> dictionary)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return GuardResult.Reject("empty output");
        }

        string trimmed = output.TrimStart();
        if (trimmed.Contains("```", StringComparison.Ordinal) ||
            trimmed.StartsWith("here is", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("here's", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("sure", StringComparison.OrdinalIgnoreCase) && !raw.TrimStart().StartsWith("sure", StringComparison.OrdinalIgnoreCase))
        {
            return GuardResult.Reject("preamble or code fence");
        }

        List<string> rawWords = Words(raw);
        List<string> outWords = Words(output);
        if (outWords.Count == 0)
        {
            return GuardResult.Reject("no words in output");
        }

        Dictionary<string, List<string>> heardAs = [];
        foreach (DictionaryEntry entry in dictionary)
        {
            string glued = string.Concat(Words(entry.Word));
            if (glued.Length > 0)
            {
                heardAs[glued] = entry.AlsoHeardAs.Select(phrase => string.Concat(Words(phrase))).Where(p => p.Length > 0).Prepend(glued).ToList();
            }
        }

        HashSet<string> terms = dictionary.SelectMany(entry => Words(entry.Word)).ToHashSet(StringComparer.Ordinal);

        (List<Run> deletions, List<string> insertions) = Align(rawWords, outWords);

        bool numberSpoken = rawWords.Any(IsNumberWord);
        bool numericOut = insertions.Any(HasDigit);
        bool layoutOut = output.Contains('\n');
        bool listOut = layoutOut && ListLine().IsMatch(output);
        bool verbatim = cleanupLevel == CleanupLevels.Verbatim;
        bool aggressive = cleanupLevel == CleanupLevels.Aggressive;
        HashSet<string> spoken = rawWords.ToHashSet(StringComparer.Ordinal);

        // A retraction moves a word: "tell Daniel... actually tell Elena" becomes "tell Elena".
        bool retracted = deletions.Any(run => run.HasRetraction);

        // Insertions: everything the model put in that the speaker did not say.
        int functionInsertions = 0;
        int repairs = 0;
        HashSet<string> repaired = [];
        foreach (string word in insertions)
        {
            // Tightening moves words around ("I was wondering if you could" -> "Could you"), which an
            // alignment sees as an insertion. A word the speaker did say is not an invention.
            if ((aggressive || retracted) && spoken.Contains(word))
            {
                continue;
            }

            if (HasDigit(word))
            {
                if (!numberSpoken)
                {
                    return GuardResult.Reject($"invented number '{word}'");
                }

                continue;
            }

            // A Dictionary term may only stand where the speaker said something close to it.
            if ((terms.Contains(word) || heardAs.ContainsKey(word)) && deletions.Any(run => SoundsLikeTerm(run, word, heardAs)))
            {
                continue;
            }

            if (FunctionWords.Contains(word))
            {
                functionInsertions++;
                continue;
            }

            if (!verbatim && TryRepair(word, deletions) is { } mended)
            {
                repairs++;
                repaired.Add(mended);
                continue;
            }

            return GuardResult.Reject($"invented word '{word}'");
        }

        if (functionInsertions > MaxInsertions)
        {
            return GuardResult.Reject("too many inserted words");
        }

        if (repairs > MaxRepairs)
        {
            return GuardResult.Reject("too many reworded words");
        }

        HashSet<string> keptOutput = outWords.ToHashSet(StringComparer.Ordinal);

        foreach (Run run in deletions)
        {
            List<string> judged = run.Words.Where(word => !repaired.Contains(word)).ToList();
            bool leadsIntoRetraction = deletions.Any(other =>
                other.HasRetraction && other.Start >= run.Start + run.Words.Count && other.Start - (run.Start + run.Words.Count) <= LeadInWords);
            GuardResult verdict = JudgeDeletion(
                run with { Words = judged, HasRetraction = run.HasRetraction || leadsIntoRetraction }, rawWords, keptOutput, numericOut || layoutOut, layoutOut, listOut, heardAs, outWords, verbatim, aggressive);
            if (!verdict.Accepted)
            {
                return verdict;
            }
        }

        if (aggressive)
        {
            // Spoken layout commands are not content, so turning them into layout is not tightening.
            int contentWords = Math.Max(1, rawWords.Count - (layoutOut ? rawWords.Count(LayoutWords.Contains) : 0));
            double ratio = (double)outWords.Count / contentWords;
            if (ratio < AggressiveMinRatio || ratio > AggressiveMaxRatio)
            {
                return GuardResult.Reject($"length ratio {ratio:0.00} outside {AggressiveMinRatio}-{AggressiveMaxRatio}");
            }

            // A name or a number the speaker said stays, unless a retraction removed it.
            foreach (string token in ProtectedTokens(raw))
            {
                string normalised = Normalise(token);
                bool removedByRetraction = deletions.Any(run => run.HasRetraction && run.Words.Contains(normalised));
                if (normalised.Length > 0 && !keptOutput.Contains(normalised) && !removedByRetraction && !repaired.Contains(normalised))
                {
                    return GuardResult.Reject($"dropped '{token}'");
                }
            }
        }

        return GuardResult.Accept;
    }

    private static GuardResult JudgeDeletion(
        Run run,
        List<string> rawWords,
        HashSet<string> keptOutput,
        bool numericOut,
        bool layoutOut,
        bool listOut,
        Dictionary<string, List<string>> heardAs,
        List<string> outWords,
        bool verbatim,
        bool aggressive)
    {
        if (run.Words.Count == 0)
        {
            return GuardResult.Accept;
        }

        // Digits for number words ("three thirty" -> "3:30"): the words the digits stand for.
        if (numericOut && run.Words.All(IsNumberWord))
        {
            return GuardResult.Accept;
        }

        // "bullet point", "new paragraph", "heading": spoken layout commands, once the output has the
        // layout. In a list, the "and" before its last item goes too ("and third send", "and keep"):
        // it joined that item, which is now a line of its own.
        if (layoutOut && run.Words.All(word => LayoutWords.Contains(word) || IsNumberWord(word) || (listOut && Conjunctions.Contains(word))))
        {
            return GuardResult.Accept;
        }

        // "blue peak" -> "Bluepeak": a run that, glued, is a Dictionary term now in the output.
        string gluedRun = string.Concat(run.Words);
        if (heardAs.Values.Any(heard => heard.Contains(gluedRun)) ||
            (run.Words.Count > 1 && outWords.Contains(gluedRun)) ||
            outWords.Any(word => heardAs.ContainsKey(word) && SoundsLikeTerm(run, word, heardAs)))
        {
            return GuardResult.Accept;
        }

        if (verbatim)
        {
            return GuardResult.Reject($"word for word, but '{string.Join(' ', run.Words)}' was removed");
        }

        if (run.HasRetraction && run.Words.Count <= MaxRetractedRun)
        {
            return GuardResult.Accept;
        }

        if (aggressive)
        {
            return GuardResult.Accept;
        }

        // Light and standard: fillers, repeats, a false start and a list's "and" that became a comma.
        if (IsFalseStart(run, rawWords))
        {
            return GuardResult.Accept;
        }

        foreach (string word in run.Words)
        {
            bool filler = (Fillers.Contains(word) || IsHedgeOf(word, run)) && run.Words.Count <= 4;
            bool repeat = keptOutput.Contains(word) && run.Words.Count <= 2 && IsAdjacentRepeat(run, rawWords);
            bool comma = Conjunctions.Contains(word) && keptOutput.Contains(word) && run.Words.Count == 1;
            if (!filler && !repeat && !comma)
            {
                return GuardResult.Reject($"dropped '{word}'");
            }
        }

        return GuardResult.Accept;
    }

    /// <summary>The "of" in "kind of" and "sort of".</summary>
    private static bool IsHedgeOf(string word, Run run)
    {
        int at = run.Words.IndexOf(word);
        return word == "of" && at > 0 && run.Words[at - 1] is "kind" or "sort";
    }

    private static bool IsAdjacentRepeat(Run run, List<string> rawWords)
    {
        int before = run.Start - 1;
        int after = run.Start + run.Words.Count;
        string first = run.Words[0];
        return (before >= 0 && rawWords[before] == first) || (after < rawWords.Count && rawWords[after] == first);
    }

    /// <summary>"Most of it was m most of it made sense": a short run the speaker abandoned and
    /// started again, so the word right after it is the run's first word.</summary>
    private static bool IsFalseStart(Run run, List<string> rawWords)
    {
        int after = run.Start + run.Words.Count;
        if (run.Words.Count is < 2 or > 6)
        {
            return false;
        }

        bool restartsAfter = after < rawWords.Count && rawWords[after] == run.Words[0];

        // The alignment may keep the first attempt and drop the second: the run then ends with the
        // two words just before it.
        bool repeatsBefore = run.Start >= 2 && run.Words.Count >= 3 &&
                             run.Words[^2] == rawWords[run.Start - 2] && run.Words[^1] == rawWords[run.Start - 1];
        return restartsAfter || repeatsBefore;
    }

    /// <summary>A word the model wrote that is a letter or two off a word the speaker said and the
    /// model dropped: the recogniser's slip, mended. Returns the dropped word, or null.</summary>
    private static string? TryRepair(string word, List<Run> deletions)
    {
        if (word.Length < MinRepairLength)
        {
            return null;
        }

        return deletions
            .SelectMany(run => run.Words)
            .FirstOrDefault(dropped => dropped.Length >= MinRepairLength && dropped != word &&
                                       EditDistance(dropped, word) <= MaxRepairDistance);
    }

    /// <summary>True when some word, or some run of up to three adjacent words, of the dropped span
    /// sounds like the Dictionary term (or like something it is "also heard as").</summary>
    private static bool SoundsLikeTerm(Run run, string term, Dictionary<string, List<string>> heardAs)
    {
        List<string> candidates = heardAs.TryGetValue(term, out List<string>? known) ? known : [term];
        for (int start = 0; start < run.Words.Count; start++)
        {
            string glued = string.Empty;
            for (int length = 1; length <= 3 && start + length <= run.Words.Count; length++)
            {
                glued += run.Words[start + length - 1];
                if (candidates.Any(candidate => EditDistance(glued, candidate) <= Math.Max(2, candidate.Length / 3)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static int EditDistance(string a, string b)
    {
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int[] current = new int[b.Length + 1];
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }

    private static bool IsNumberWord(string word) => NumberWords.Contains(word) || HasDigit(word);

    private static bool HasDigit(string word) => word.Any(char.IsDigit);

    /// <summary>The capitalised (not sentence-opening) and numeric tokens of the raw text.</summary>
    private static IEnumerable<string> ProtectedTokens(string raw)
    {
        string[] tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool sentenceStart = true;
        foreach (string token in tokens)
        {
            string letters = Regex.Replace(token, @"[^\p{L}\p{N}']", string.Empty);
            bool isNumeric = letters.Any(char.IsDigit);
            bool isCapitalised = letters.Length > 1 && char.IsUpper(letters[0]) && !sentenceStart && letters != "I";
            if (letters.Length > 0 && (isNumeric || isCapitalised))
            {
                yield return letters;
            }

            sentenceStart = token.EndsWith('.') || token.EndsWith('?') || token.EndsWith('!');
        }
    }

    /// <summary>Lower-cased words: punctuation gone, contractions spelled out, apostrophes dropped.</summary>
    internal static List<string> Words(string text)
    {
        string spelled = text.ToLowerInvariant().Replace('’', '\'');
        spelled = spelled.Replace("won't", "will not").Replace("can't", "can not").Replace("cannot", "can not");
        spelled = ContractionPattern().Replace(spelled, match => match.Value switch
        {
            "n't" => " not",
            "'ve" => " have",
            "'ll" => " will",
            "'re" => " are",
            "'m" => " am",
            _ => match.Value,
        });
        spelled = PronounIs().Replace(spelled, "${pronoun} is");
        spelled = spelled.Replace("'", string.Empty);

        StringBuilder cleaned = new(spelled.Length);
        foreach (char c in spelled)
        {
            cleaned.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        // The Formal Style's word swaps (ticket 56) count as the same words, so a model that makes
        // them itself ("yeah" as "yes") is not taken to have invented one.
        return cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(word => FormalSpelling.TryGetValue(word, out string[]? formal) ? formal : [word])
            .ToList();
    }

    private static readonly Dictionary<string, string[]> FormalSpelling = new(StringComparer.Ordinal)
    {
        ["yeah"] = ["yes"],
        ["yep"] = ["yes"],
        ["yup"] = ["yes"],
        ["nope"] = ["no"],
        ["nah"] = ["no"],
        ["gonna"] = ["going", "to"],
        ["wanna"] = ["want", "to"],
        ["gotta"] = ["have", "to"],
        ["kinda"] = ["kind", "of"],
        ["sorta"] = ["sort", "of"],
    };

    /// <summary>"it's" and the like, which Formal writes "it is".</summary>
    [GeneratedRegex(@"\b(?<pronoun>it|that|there|here|what|who|where|how|he|she|when|why|this)'s\b")]
    private static partial Regex PronounIs();

    private static string Normalise(string token) => string.Concat(Words(token));

    /// <summary>Words of <paramref name="raw"/> missing from the output (grouped into runs), and
    /// words of the output missing from <paramref name="raw"/>, by longest common subsequence.</summary>
    private static (List<Run> Deletions, List<string> Insertions) Align(List<string> raw, List<string> output)
    {
        int n = raw.Count;
        int m = output.Count;
        int[,] lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = raw[i] == output[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        List<string> insertions = [];
        bool[] rawKept = new bool[n];
        int a = 0;
        int b = 0;
        while (a < n && b < m)
        {
            if (raw[a] == output[b])
            {
                rawKept[a] = true;
                a++;
                b++;
            }
            else if (lcs[a + 1, b] >= lcs[a, b + 1])
            {
                a++;
            }
            else
            {
                insertions.Add(output[b]);
                b++;
            }
        }

        while (b < m)
        {
            insertions.Add(output[b++]);
        }

        List<Run> runs = [];
        for (int i = 0; i < n; i++)
        {
            if (rawKept[i])
            {
                continue;
            }

            int start = i;
            List<string> words = [];
            while (i < n && !rawKept[i])
            {
                words.Add(raw[i]);
                i++;
            }

            i--;
            runs.Add(new Run(start, words, ContainsMarker(raw, start, words.Count)));
        }

        return (runs, insertions);
    }

    private static bool ContainsMarker(List<string> raw, int start, int count)
    {
        string span = string.Join(' ', raw.Skip(start).Take(count));
        return RetractionMarkers.Any(marker => ($" {span} ").Contains($" {marker} ", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?<=\w)n't|'ve|'ll|'re|'m")]
    private static partial Regex ContractionPattern();

    [GeneratedRegex(@"^\s*(?:\d+[.)]|[-*])\s", RegexOptions.Multiline)]
    private static partial Regex ListLine();

    private sealed record Run(int Start, List<string> Words, bool HasRetraction);
}
