using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Deterministic, regex-based cleanup for offline transcription output. The local ASR model is
/// transcription-only: no instruction-following, no self-correction handling, unlike the prompt
/// rules in <see cref="GeminiTranscriptionClient"/>. This closes part of that gap cheaply and
/// predictably; it is not a substitute for the Online-mode cleanup quality.
/// </summary>
public static partial class OfflineTextPostProcessor
{
    public static string Clean(string rawText)
    {
        string text = rawText.Trim();
        text = FillerWordPattern().Replace(text, " ");
        text = RepeatedWordPattern().Replace(text, "$1");
        text = WhitespacePattern().Replace(text, " ").Trim();

        if (text.Length == 0)
        {
            return text;
        }

        text = char.ToUpperInvariant(text[0]) + text[1..];

        if (!EndsWithPunctuationPattern().IsMatch(text))
        {
            text += ".";
        }

        return text;
    }

    [GeneratedRegex(@"\b(um+|uh+|erm+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FillerWordPattern();

    [GeneratedRegex(@"\b(\w+)(\s+\1\b)+", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedWordPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[.!?]$")]
    private static partial Regex EndsWithPunctuationPattern();
}
