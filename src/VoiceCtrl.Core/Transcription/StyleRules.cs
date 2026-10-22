using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// What a <see cref="Style"/> does to the finished text (ticket 56), in code on either engine's text,
/// so Online, Offline and a message too short for Smart cleanup all read the same. The layout is
/// already done; this changes capitals, closing punctuation and register only.
/// <list type="bullet">
/// <item>Formal: contractions written out ("I'll" is "I will") and casual words made formal ("yeah"
/// is "yes", "gonna" is "going to").</item>
/// <item>Casual: a line loses its closing full stop.</item>
/// <item>Very casual: that, and sentences start lowercase. "I" and names keep their capitals.</item>
/// <item>Excited: a sentence ends with "!" instead of a full stop.</item>
/// </list>
/// Formal changes words, so Word for word skips it: there, every word stays as said.
/// </summary>
internal static partial class StyleRules
{
    /// <param name="isOrdinaryWord">Whether a lower-case word is ordinary English rather than a name,
    /// for Very casual: "Sarah" keeps her capital, "Sounds" does not. Null lowercases no word.</param>
    public static string Apply(string text, Style style, string cleanupLevel, Func<string, bool>? isOrdinaryWord = null) => style switch
    {
        Style.Formal => cleanupLevel == CleanupLevels.Verbatim ? text : Formal(text),
        Style.Casual => CasualChatPunctuation.Apply(text),
        Style.VeryCasual => LowercaseSentenceStarts(CasualChatPunctuation.Apply(text), isOrdinaryWord),
        Style.Excited => Excited(text),
        _ => text,
    };

    private static readonly Dictionary<string, string> FormalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yeah"] = "yes",
        ["yep"] = "yes",
        ["yup"] = "yes",
        ["nope"] = "no",
        ["nah"] = "no",
        ["gonna"] = "going to",
        ["wanna"] = "want to",
        ["gotta"] = "have to",
        ["kinda"] = "kind of",
        ["sorta"] = "sort of",
        ["won't"] = "will not",
        ["can't"] = "cannot",
        ["shan't"] = "shall not",
        ["ain't"] = "is not",
        ["i'm"] = "I am",
    };

    /// <summary>"'s" after these is "is" (or "has" before "been"); after a name it is a possessive.</summary>
    private static readonly HashSet<string> IsPronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "it", "that", "there", "here", "what", "who", "where", "how", "he", "she", "when", "why", "this",
    };

    /// <summary>"'d" before these is "had"; before anything else, "would".</summary>
    private static readonly HashSet<string> HadNext = new(StringComparer.OrdinalIgnoreCase)
    {
        "been", "better", "already", "never", "just",
    };

    private static string Formal(string text) => Contraction().Replace(text, match =>
    {
        // Matched with either apostrophe; looked up with the straight one. Both are one character.
        string word = match.Groups["word"].Value.Replace('’', '\'');
        string next = match.Groups["next"].Value;
        string replacement;
        if (FormalWords.TryGetValue(word, out string? plain))
        {
            replacement = plain;
        }
        else if (word.EndsWith("n't", StringComparison.OrdinalIgnoreCase))
        {
            replacement = word[..^3] + " not";
        }
        else if (word.EndsWith("'ll", StringComparison.OrdinalIgnoreCase))
        {
            replacement = word[..^3] + " will";
        }
        else if (word.EndsWith("'re", StringComparison.OrdinalIgnoreCase))
        {
            replacement = word[..^3] + " are";
        }
        else if (word.EndsWith("'ve", StringComparison.OrdinalIgnoreCase))
        {
            replacement = word[..^3] + " have";
        }
        else if (word.EndsWith("'d", StringComparison.OrdinalIgnoreCase))
        {
            replacement = word[..^2] + (HadNext.Contains(next) ? " had" : " would");
        }
        else if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) && IsPronouns.Contains(word[..^2]))
        {
            bool has = next.Equals("been", StringComparison.OrdinalIgnoreCase) || next.Equals("got", StringComparison.OrdinalIgnoreCase);
            replacement = word[..^2] + (has ? " has" : " is");
        }
        else
        {
            return match.Value;
        }

        return WithCaseOf(word, replacement);
    });

    /// <summary>"Yeah" becomes "Yes", "yeah" "yes"; "I" stays a capital wherever it lands.</summary>
    private static string WithCaseOf(string original, string replacement)
    {
        if (replacement.StartsWith("I ", StringComparison.Ordinal))
        {
            return replacement;
        }

        return char.IsUpper(original[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
    }

    private static string Excited(string text) => SentenceFullStop().Replace(text, match =>
        IsAbbreviation(text, match.Index) ? match.Value : "!");

    private static bool IsAbbreviation(string text, int fullStop)
    {
        int start = fullStop;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        return CasualChatPunctuation.IsAbbreviation(text[start..(fullStop + 1)]);
    }

    private static string LowercaseSentenceStarts(string text, Func<string, bool>? isOrdinaryWord) =>
        isOrdinaryWord is null ? text : SentenceStart().Replace(text, match =>
        {
            string word = match.Groups["word"].Value;
            string stem = word.Split('\'', '’')[0];
            bool keepsCapital = stem == "I" || stem.Length > 1 && stem.All(char.IsUpper) || !isOrdinaryWord(stem.ToLowerInvariant());
            return keepsCapital ? match.Value : match.Groups["lead"].Value + char.ToLowerInvariant(word[0]) + word[1..];
        });

    /// <summary>A word with an apostrophe, or one of the casual words; the word after it is only looked at.</summary>
    [GeneratedRegex(@"\b(?<word>[\p{L}]+['’][\p{L}]+|(?i:yeah|yep|yup|nope|nah|gonna|wanna|gotta|kinda|sorta))\b(?=\s*(?<next>[\p{L}]*))")]
    private static partial Regex Contraction();

    /// <summary>A full stop that ends a sentence: not one of "...", nor inside "3.5" or "e.g.".</summary>
    [GeneratedRegex(@"(?<![.\d])\.(?![.\w])")]
    private static partial Regex SentenceFullStop();

    /// <summary>A sentence's first word: at a line's start (after a list marker) or after ". ", "? ", "! ".</summary>
    [GeneratedRegex(@"(?<lead>(?:^|(?<=[.!?]\s))(?:(?:\d+[.)]|[-*•])\s+)?)(?<word>\p{Lu}[\p{L}'’]*)", RegexOptions.Multiline)]
    private static partial Regex SentenceStart();
}
