using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Personalization;

/// <summary>A word the miner found: how many Dictations it was said in, and one sentence it was
/// said in (the context TypeSafe judges it by).</summary>
public sealed record SuggestionCandidate(string Word, int Dictations, string Example);

/// <summary>
/// Finds Dictionary Suggestion candidates in the kept Dictations with plain code (research 06,
/// candidate c): names and terms by their shape, never by meaning. Counting and spotting shapes is
/// work for code; telling "Maya Ortiz" from "Zoom" is the judgment TypeSafe adds on top
/// (<see cref="DictionarySuggestions"/>).
/// <para>
/// A candidate is either a run of up to three capitalised words said mid-sentence ("Maya Ortiz"),
/// or one word whose shape no ordinary word has: an acronym, mixed case or letters with digits
/// ("AGIFORS", "VoiceCtrl", "A320"). A capital at the start of a sentence proves nothing, so a word
/// only counts there when the same run was also said mid-sentence. A capitalised word that is also
/// used in lowercase ("Glide" and "glide") is an ordinary word.
/// </para>
/// </summary>
public static partial class SuggestionMiner
{
    public const int MaxExampleLength = 160;

    private const int MaxRunWords = 3;

    /// <summary>
    /// Capitalised or shaped words that are never worth a Dictionary entry: the recognizer already
    /// spells them, and they are said capitalised mid-sentence all the time.
    /// </summary>
    private static readonly HashSet<string> NeverSuggested = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "i'm", "i'll", "i've", "i'd",
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
        "january", "february", "march", "april", "may", "june", "july", "august", "september",
        "october", "november", "december",
        "mr", "mrs", "ms", "dr", "prof", "sir", "madam",
        "ok", "okay", "am", "pm", "tv", "id", "pdf", "url", "faq", "fyi", "asap", "eta", "ceo",
        "cto", "cfo", "hr", "pr", "it", "ai", "api", "ui", "ux", "usb", "pc", "uk", "us", "usa",
        "eu", "un", "nyc", "gmt", "utc", "cet", "q1", "q2", "q3", "q4", "wifi", "tl", "dr",
        "the", "a", "an", "and", "or", "but", "of", "to", "in", "on", "at", "for", "with", "by",
        "hello", "hi", "hey", "dear", "thanks", "yes", "no", "please",
        "english", "internet",
    };

    /// <summary>
    /// Finds the candidates in <paramref name="dictations"/> (newest first, as the history lists
    /// them), most recurring first. <paramref name="exclude"/> is what must never come back: the
    /// Dictionary's words and phrases and the ignored suggestions, compared ignoring case. A
    /// candidate that is one of the words of an excluded term ("Ortiz" of "Maya Ortiz") is left out
    /// too.
    /// </summary>
    public static IReadOnlyList<SuggestionCandidate> Find(IEnumerable<string> dictations, IReadOnlyCollection<string> exclude)
    {
        var excluded = new HashSet<string>(exclude.Select(Normalize), StringComparer.Ordinal);
        var excludedWords = new HashSet<string>(
            exclude.SelectMany(term => WordPattern().Matches(term).Select(m => m.Value.ToLowerInvariant())),
            StringComparer.Ordinal);

        List<(int Dictation, string Text, List<Run> Runs)> parsed = [];
        var lowercaseWords = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (string text in dictations)
        {
            parsed.Add((index++, text, Parse(text, lowercaseWords)));
        }

        // Runs said mid-sentence somewhere, so the same run starting a sentence counts too.
        var midSentence = new HashSet<string>(
            parsed.SelectMany(p => p.Runs).Where(r => !r.StartsSentence).Select(r => Normalize(r.Text)),
            StringComparer.Ordinal);

        var tallies = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach ((int dictation, string text, List<Run> runs) in parsed)
        {
            foreach (Run run in runs)
            {
                Run? counted = Countable(run, midSentence);
                if (counted is null)
                {
                    continue;
                }

                string key = Normalize(counted.Text);
                if (excluded.Contains(key) || counted.Words.All(w => excludedWords.Contains(w.ToLowerInvariant())))
                {
                    continue;
                }

                // One capitalised word also said in lowercase is an ordinary word.
                if (counted.Words.Count == 1 && !counted.Shaped && lowercaseWords.Contains(key))
                {
                    continue;
                }

                if (!tallies.TryGetValue(key, out Tally? tally))
                {
                    tally = new Tally(Example(text, counted.Start, counted.Text.Length));
                    tallies[key] = tally;
                }

                tally.Dictations.Add(dictation);
                tally.Spellings[counted.Text] = tally.Spellings.GetValueOrDefault(counted.Text) + 1;
            }
        }

        return tallies.Values
            .Select(t => new SuggestionCandidate(
                t.Spellings.OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key,
                t.Dictations.Count,
                t.Example))
            .OrderByDescending(c => c.Dictations)
            .ThenBy(c => c.Word, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The part of <paramref name="run"/> that counts, or null. A run starting a sentence
    /// loses its first word unless that word is shaped, or the whole run was also said mid-sentence.</summary>
    private static Run? Countable(Run run, HashSet<string> midSentence)
    {
        Run counted = run;
        if (run.StartsSentence && !run.FirstShaped && !midSentence.Contains(Normalize(run.Text)))
        {
            if (run.Words.Count == 1)
            {
                return null;
            }

            counted = run.WithoutFirstWord();
        }

        return counted.Words.Count <= MaxRunWords && counted.Text.Length <= CustomDictionary.MaxTermLength ? counted : null;
    }

    /// <summary>Splits one Dictation into runs, and notes every word it uses in lowercase.</summary>
    private static List<Run> Parse(string text, HashSet<string> lowercaseWords)
    {
        var runs = new List<Run>();
        var words = new List<(string Word, int Start, bool Shaped)>();
        bool runStartsSentence = false;
        int previousEnd = 0;
        bool atSentenceStart = true;

        void Close()
        {
            if (words.Count > 0)
            {
                runs.Add(new Run(text, words.ToList(), runStartsSentence));
                words.Clear();
            }
        }

        foreach (Match match in TokenPattern().Matches(text))
        {
            string gap = text[previousEnd..match.Index];
            if (gap.AsSpan().IndexOfAny(SentenceEnds) >= 0)
            {
                atSentenceStart = true;
            }

            // Only a plain space keeps a run going; any punctuation or line break ends it.
            if (gap.Length == 0 || gap.Any(c => c != ' '))
            {
                Close();
            }

            string word = StripPossessive(match.Value);
            previousEnd = match.Index + match.Length;
            bool startsSentence = atSentenceStart;
            atSentenceStart = false;

            if (!word.Any(char.IsLetter))
            {
                Close();
                continue;
            }

            bool shaped = IsShaped(word);
            if (!shaped && !char.IsUpper(word[0]))
            {
                lowercaseWords.Add(word.ToLowerInvariant());
                Close();
                continue;
            }

            if (word.Length < 2 || NeverSuggested.Contains(word))
            {
                Close();
                continue;
            }

            // Shaped words starting in lowercase ("iPhone") stand alone rather than join a run.
            if (!char.IsUpper(word[0]))
            {
                Close();
                runs.Add(new Run(text, [(word, match.Index, true)], startsSentence));
                continue;
            }

            if (words.Count == 0)
            {
                runStartsSentence = startsSentence;
            }

            words.Add((word, match.Index, shaped));
        }

        Close();
        return runs;
    }

    /// <summary>An acronym, mixed case, or letters with digits: a shape no ordinary word has.</summary>
    private static bool IsShaped(string word) =>
        word.Any(char.IsDigit) || word.Skip(1).Any(char.IsUpper);

    private static string StripPossessive(string word) =>
        word.Length > 2 && (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || word.EndsWith("’s", StringComparison.OrdinalIgnoreCase))
            ? word[..^2]
            : word;

    /// <summary>The sentence the word was said in, cut to a window around it when long.</summary>
    private static string Example(string text, int start, int length)
    {
        int from = text.LastIndexOfAny(SentenceEnds, Math.Max(0, start - 1)) + 1;
        int to = text.IndexOfAny(SentenceEnds, start + length);
        to = to < 0 ? text.Length : to + 1;
        string sentence = text[from..to].Trim();
        if (sentence.Length <= MaxExampleLength)
        {
            return sentence;
        }

        int wordAt = start - from - (text[from..to].Length - text[from..to].TrimStart().Length);
        int windowStart = Math.Clamp(wordAt + length / 2 - MaxExampleLength / 2, 0, sentence.Length - MaxExampleLength);
        string window = sentence.Substring(windowStart, MaxExampleLength);

        // Cut at word boundaries, so no half words reach the judge.
        int firstSpace = windowStart > 0 ? window.IndexOf(' ') : -1;
        int lastSpace = windowStart + MaxExampleLength < sentence.Length ? window.LastIndexOf(' ') : -1;
        if (lastSpace > firstSpace + 1)
        {
            window = window[..lastSpace];
        }

        return (firstSpace >= 0 && firstSpace < window.Length ? window[(firstSpace + 1)..] : window).Trim();
    }

    private static string Normalize(string term) => string.Join(' ', WordPattern().Matches(term).Select(m => m.Value)).ToLowerInvariant();

    private static readonly char[] SentenceEnds = ['.', '!', '?', ':', ';', '\n', '\r', '•'];

    private sealed record Run(string Source, IReadOnlyList<(string Word, int Start, bool Shaped)> Parts, bool StartsSentence)
    {
        public IReadOnlyList<string> Words => Parts.Select(p => p.Word).ToList();

        public int Start => Parts[0].Start;

        public string Text => string.Join(' ', Words);

        public bool Shaped => Parts.Count == 1 && Parts[0].Shaped;

        public bool FirstShaped => Parts[0].Shaped;

        public Run WithoutFirstWord() => new(Source, Parts.Skip(1).ToList(), StartsSentence: false);
    }

    private sealed class Tally(string example)
    {
        public string Example { get; } = example;

        public HashSet<int> Dictations { get; } = [];

        public Dictionary<string, int> Spellings { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Letters and digits, joined by apostrophes or hyphens ("Jean-Luc", "Bluepeak's").</summary>
    [GeneratedRegex(@"[\p{L}\p{Nd}]+(?:['’\-][\p{L}\p{Nd}]+)*")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex WordPattern();
}
