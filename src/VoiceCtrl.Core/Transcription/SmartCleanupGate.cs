using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Decides whether the local model can add anything to a transcript the rules have already cleaned
/// (research 33 §4, lever 3). Every skipped call saves the 2-4 s Smart cleanup costs, and a model
/// that is not asked cannot invent a word. Parakeet's text is already punctuated and capitalised, so
/// a plain fluent sentence gains little.
/// </summary>
internal static partial class SmartCleanupGate
{
    private const int MinWords = 5;

    /// <summary>An email's or a document's text this long can need a paragraph break (ticket 55); a
    /// shorter one is one paragraph, and asking the model costs it about 6 s on a laptop.</summary>
    private const int MinParagraphingWords = 40;

    public static bool ShouldRun(string ruleCleanedText, PromptContext context)
    {
        int words = WordCount(ruleCleanedText);
        if (words < MinWords)
        {
            return false;
        }

        if (context.CleanupLevel == CleanupLevels.Aggressive)
        {
            return true;
        }

        bool paragraphs = context.EmailLayout || FormattingHintMapper.IsParagraphs(context.FormattingHint) ||
                          context.Chat == ChatLayout.Paragraphs;
        if (paragraphs && words >= MinParagraphingWords)
        {
            return true;
        }

        // An AI prompt's hint (or a Profile's own) asks the model to find items the speaker did not
        // mark. A chat's paragraph, an email's greeting and sign-off are set in code (PasteLayout), so a
        // short chat message or email with nothing else to do no longer waits for the model.
        bool asksForStructure = context.FormattingHint is { } hint && !FormattingHintMapper.IsProse(hint) &&
                                (hint.Contains("bullet", StringComparison.OrdinalIgnoreCase) ||
                                 hint.Contains("numbered", StringComparison.OrdinalIgnoreCase));

        // Word for word keeps every word; only layout could still change.
        if (context.CleanupLevel == CleanupLevels.Verbatim && !asksForStructure && !FormattingHintMapper.IsProse(context.FormattingHint))
        {
            return false;
        }

        return asksForStructure || SpokenEditPattern().IsMatch(ruleCleanedText) || OpeningFillerPattern().IsMatch(ruleCleanedText);
    }

    /// <summary>"So," "Okay so" "Well," opening the text: a filler only the model can tell from a word
    /// that belongs ("So the plan is" vs "So, quick update").</summary>
    [GeneratedRegex(@"^(?:so|okay|ok|well|alright)\b[,\s]", RegexOptions.IgnoreCase)]
    private static partial Regex OpeningFillerPattern();

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Things a rule cannot settle: a retraction, an enumeration, a spoken layout command, or
    /// a filler the rules leave alone ("like", "you know", "I mean", "basically"). An enumeration is
    /// ordinals, a counted lead-in ("three things") or bare numerals ("one X, two Y"), in any app.</summary>
    [GeneratedRegex(
        @"\b(?:actually|no wait|wait no|wait|scratch that|make that|i mean|sorry|correction|first|second|third|" +
        @"bullet points?|numbered list|new (?:paragraph|line)|heading|you know|like|basically|kind of|sort of|" +
        @"number one|number two|percent|dollars?|euros?|pounds?|thousand|million|" +
        @"o'?clock|(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve) (?:thirty|fifteen|forty[- ]five|oh \w+)|" +
        @"one\W+\w+\W+two|one\b[^.!?]{2,}?[,;]\s*two|" +
        @"(?:two|three|four|five|six|seven|eight|nine|ten) (?:things|items|points|steps|tasks|reasons|questions|options|ideas|issues|topics))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpokenEditPattern();
}
