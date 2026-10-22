using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// "a" or "an" as the next word calls for ("an data agreement" becomes "a data agreement"). It acts
/// only on ordinary lower-case words, and skips every opening that English does not spell as it
/// sounds (hour, university, one, an MBA), so a wrong guess is rarer than the slip it mends.
/// </summary>
internal static partial class ArticleAgreement
{
    private const string Vowels = "aeiou";

    /// <summary>Letters that open a word with a silent or unusual sound often enough to leave alone.</summary>
    private const string UnreliableOpenings = "hux";

    private static readonly string[] ConsonantSoundsStartingWithAVowel = ["one", "once", "euro", "eu"];

    public static string Apply(string text, Func<string, bool> isOrdinaryWord) =>
        ArticlePattern().Replace(text, match =>
        {
            string article = match.Groups["article"].Value;
            string next = match.Groups["next"].Value;
            if (!isOrdinaryWord(next.ToLowerInvariant()) || !char.IsLower(next[0]))
            {
                return match.Value;
            }

            string fixedArticle = ArticleFor(next, article.ToLowerInvariant());
            return fixedArticle == article.ToLowerInvariant()
                ? match.Value
                : (char.IsUpper(article[0]) ? char.ToUpperInvariant(fixedArticle[0]) + fixedArticle[1..] : fixedArticle) + match.Groups["gap"].Value + next;
        });

    private static string ArticleFor(string next, string current)
    {
        char first = char.ToLowerInvariant(next[0]);
        if (UnreliableOpenings.Contains(first) || ConsonantSoundsStartingWithAVowel.Any(prefix => next.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return current;
        }

        return Vowels.Contains(first) ? "an" : "a";
    }

    [GeneratedRegex(@"(?<![\p{L}'])(?<article>[Aa]n?)(?<gap> +)(?<next>\p{L}+)\b")]
    private static partial Regex ArticlePattern();
}
