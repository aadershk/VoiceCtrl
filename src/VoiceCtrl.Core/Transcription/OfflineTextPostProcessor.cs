using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Deterministic, regex-based cleanup for offline transcription output. The local ASR model is
/// transcription-only: no instruction-following, no self-correction handling, unlike the prompt
/// rules in <see cref="GeminiTranscriptionClient"/>. This closes part of that gap cheaply and
/// predictably; it is not a substitute for the Online-mode cleanup quality.
///
/// Two things Online does are deliberately not attempted here. Self-correction ("send it to John,
/// actually, no, Sarah") needs to know which span the speaker retracted, and spoken-number
/// normalisation needs to know whether "twenty twenty four" is a year, a quantity or an address.
/// Both are judgement calls a regex cannot make, and a wrong guess silently corrupts meaning,
/// which is worse than leaving the words alone.
/// </summary>
public static partial class OfflineTextPostProcessor
{
    private static readonly Dictionary<string, string> SpokenPunctuation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["comma"] = ",",
        ["period"] = ".",
        ["full stop"] = ".",
        ["question mark"] = "?",
        ["exclamation mark"] = "!",
        ["exclamation point"] = "!",
        ["colon"] = ":",
        ["semicolon"] = ";",
        ["semi colon"] = ";",
    };

    /// <param name="applySpokenPunctuation">
    /// Turns spoken "comma"/"period"/"question mark" into the characters themselves. Off by
    /// default and gated by SPOKEN_PUNCTUATION because the words are ordinary English: "the comma
    /// is missing" must not become "the , is missing". Line breaks are handled separately and
    /// unconditionally, since "new paragraph" is far more likely to be a command than a phrase,
    /// and the guards on those two patterns cover the cases where it is not.
    /// </param>
    public static string Clean(string rawText, bool applySpokenPunctuation = false)
    {
        string text = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        text = FillerWordPattern().Replace(text, " ");
        text = RepeatedWordPattern().Replace(text, "$1");

        // Collapsed early so every pattern below can assume single spaces, which is what lets the
        // break-command guards use a fixed-width lookbehind instead of an unbounded one.
        text = HorizontalWhitespacePattern().Replace(text, " ");

        if (applySpokenPunctuation)
        {
            text = SpokenPunctuationPattern().Replace(
                text, match => SpokenPunctuation.TryGetValue(match.Value, out string? symbol) ? symbol : match.Value);
        }

        text = NewParagraphPattern().Replace(text, "\n\n");
        text = NewLinePattern().Replace(text, "\n");

        text = TidyPunctuation(text);

        if (text.Length == 0)
        {
            return text;
        }

        text = StandalonePronounIPattern().Replace(text, "I");
        text = char.ToUpperInvariant(text[0]) + text[1..];
        text = SentenceStartPattern().Replace(text, match => match.Value.ToUpperInvariant());

        if (!EndsWithPunctuationPattern().IsMatch(text))
        {
            text += ".";
        }

        return text;
    }

    /// <summary>
    /// Repairs the punctuation the passes above leave stranded. Removing a filler from between two
    /// commas ("uh" in "so, uh, roll it out") leaves ", ," behind, and an utterance that opened
    /// with a filler leaves the text starting on a comma. Both were visible in real offline
    /// transcripts. Inserting a line break has the same effect at the seam, where the comma that
    /// used to join two clauses is now dangling at the end or start of a line.
    /// </summary>
    private static string TidyPunctuation(string text)
    {
        text = SpaceBeforePunctuationPattern().Replace(text, "$1");
        text = DuplicatePunctuationPattern().Replace(text, "$1");
        text = PunctuationAroundNewlinePattern().Replace(text, "$1");
        text = LeadingPunctuationPattern().Replace(text, string.Empty);
        return text.Trim();
    }

    [GeneratedRegex(@"\b(um+|uh+|erm+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FillerWordPattern();

    [GeneratedRegex(@"\b(\w+)(\s+\1\b)+", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedWordPattern();

    /// <summary>Spaces and tabs only. A blanket \s+ would flatten the line breaks inserted below
    /// back into single spaces.</summary>
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalWhitespacePattern();

    [GeneratedRegex(@"\b(?:comma|period|full stop|question mark|exclamation mark|exclamation point|colon|semicolon|semi colon)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpokenPunctuationPattern();

    // The lookbehind rejects "a new paragraph", "the new line", "my new paragraph" and friends,
    // where the words describe something rather than ask for a break. The lookahead rejects the
    // determiner-less version of the same thing, "new line of business". Without these two guards
    // an always-on break command would corrupt ordinary sentences, which is the reason Online's
    // equivalent rule can be a prompt instruction and this one cannot.
    private const string BreakCommandGuards =
        @"(?<!\b(?:a|an|the|this|that|each|every|any|another|some|one|no|my|your|his|her|its|our|their) )";

    [GeneratedRegex(BreakCommandGuards + @"\bnew paragraphs?\b(?! of\b)", RegexOptions.IgnoreCase)]
    private static partial Regex NewParagraphPattern();

    [GeneratedRegex(BreakCommandGuards + @"\bnew lines?\b(?! of\b)", RegexOptions.IgnoreCase)]
    private static partial Regex NewLinePattern();

    [GeneratedRegex(@"[^\S\n]+([,.;:!?])")]
    private static partial Regex SpaceBeforePunctuationPattern();

    [GeneratedRegex(@"([,;:])(?:[^\S\n]*[,;:])+")]
    private static partial Regex DuplicatePunctuationPattern();

    /// <summary>Absorbs the spaces and the joining comma/colon on either side of an inserted break.
    /// A sentence-ending "." on the line above is left alone: that one is still correct.</summary>
    [GeneratedRegex(@"[^\S\n]*[,;:]?[^\S\n]*(\n+)[^\S\n]*[,;:]?[^\S\n]*")]
    private static partial Regex PunctuationAroundNewlinePattern();

    [GeneratedRegex(@"^[\s,;:.!?]+")]
    private static partial Regex LeadingPunctuationPattern();

    /// <summary>The trailing (?![\w'.]) keeps "i.e." and contractions intact.</summary>
    [GeneratedRegex(@"(?<![\w'])i(?![\w'.])")]
    private static partial Regex StandalonePronounIPattern();

    /// <summary>A lowercase letter opening a new line or a new sentence. The very first character
    /// is handled directly by the caller, so this only deals with the interior of the text.
    ///
    /// The second lookbehind excludes a full stop that ends an abbreviation rather than a sentence,
    /// which otherwise turns "ship it, i.e. today" into "i.e. Today". It covers dotted initialisms
    /// (i.e., e.g., a.m.) plus the two spelled-out abbreviations that actually turn up in speech.
    /// The cost is a genuine sentence opening straight after one of those, which is the rarer case
    /// and leaves a missing capital rather than a wrong one.</summary>
    [GeneratedRegex(@"(?<=[.!?][^\S\n]|\n)(?<!(?:\.\p{L}|\betc|\bvs)\.[^\S\n])\p{Ll}")]
    private static partial Regex SentenceStartPattern();

    [GeneratedRegex(@"[.!?]$")]
    private static partial Regex EndsWithPunctuationPattern();
}
