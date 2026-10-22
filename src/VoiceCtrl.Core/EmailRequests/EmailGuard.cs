using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.EmailRequests;

/// <summary>
/// Checks an email body the model wrote from an Email request (ticket 57) for what it must never add:
/// a number, a date or day, a name or place, a link or address that the request did not give. An
/// email is meant to have words the request did not say ("Thank you for your time. I really
/// appreciate it."), so unlike Smart cleanup's guard this does not check words one by one; it checks
/// the kinds of words that would make the email say something untrue. A refused body is never pasted:
/// the request is typed as it was said instead.
/// </summary>
internal static partial class EmailGuard
{
    /// <summary>Null when <paramref name="body"/> may be sent; otherwise the kind of thing it added,
    /// for the log (never the word itself, which may be private).</summary>
    public static string? Check(string body, EmailRequest request)
    {
        string said = request.Words;
        int requestWords = Words(said);
        int bodyWords = Words(body);

        // A short request gets a short email; a long one may need several sentences to keep every detail.
        if (bodyWords > 50 + 3 * requestWords)
        {
            return "too long";
        }

        // First: a reply in the model's own voice ("As an AI...") is refused for that, whatever else it holds.
        if (AssistantVoice().IsMatch(body))
        {
            return "assistant voice";
        }

        if (IsVerse(body))
        {
            return "verse";
        }

        if (LinkOrAddress().IsMatch(body) && !LinkOrAddress().IsMatch(said))
        {
            return "link";
        }

        HashSet<string> saidNumbers = [.. Number().Matches(said).Select(match => match.Value)];
        if (Number().Matches(body).Any(match => !saidNumbers.Contains(match.Value)))
        {
            return "number";
        }

        HashSet<string> saidWords = new(Word().Matches(said).Select(match => Plain(match.Value)), StringComparer.OrdinalIgnoreCase);
        if (DayOrDate().Matches(body).Any(match => !SaidAll(match.Value, saidWords)))
        {
            return "date";
        }

        // A capitalised word in mid-sentence is a name, a place or a product: it has to be one the
        // request said (or the recipient's). "I" and its contractions are the only exceptions.
        foreach (Match capital in MidSentenceCapital().Matches(body))
        {
            string word = Plain(capital.Value);
            if (!saidWords.Contains(word) && !NotANameAlone.Contains(word))
            {
                return "name";
            }
        }

        return PromptWords().IsMatch(body) && !PromptWords().IsMatch(said) ? "prompt" : null;
    }

    /// <summary>A poem or a song, which a request can smuggle in ("... and write a poem about the
    /// sea"): four or more lines, most of them not ending a sentence. An email's lines end sentences,
    /// and a list's items are not counted.</summary>
    private static bool IsVerse(string body)
    {
        string[] lines = body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !ListItem().IsMatch(line)).ToArray();
        return lines.Length >= 4 && lines.Count(line => line[^1] is not ('.' or '!' or '?' or ':')) * 2 >= lines.Length;
    }

    [GeneratedRegex(@"^(?:\d+[.)]|[-*•])\s")]
    private static partial Regex ListItem();

    private static readonly HashSet<string> NotANameAlone = new(StringComparer.Ordinal) { "I", "OK", "Ok" };

    private static bool SaidAll(string phrase, HashSet<string> saidWords) =>
        Word().Matches(phrase).All(match => saidWords.Contains(Plain(match.Value)));

    /// <summary>A word without its possessive: "Mark's" counts as "Mark".</summary>
    private static string Plain(string word) => PossessiveOrContraction().Replace(word, string.Empty);

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’-]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"['’](?:s|ll|m|d|ve|re)$", RegexOptions.IgnoreCase)]
    private static partial Regex PossessiveOrContraction();

    /// <summary>The digits of a number, a time or an amount: "3", "14", "2,000" as "2" and "000".</summary>
    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();

    [GeneratedRegex(@"https?://|www\.|\b[\w.+-]+@[\w-]+\.[\w.]+|\.(?:com|org|net|io|co)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LinkOrAddress();

    /// <summary>Days, months and the words that place a thing in time ("tomorrow", "next week").</summary>
    [GeneratedRegex(
        @"\b(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|weekend|january|february|march|april|june|july|august|" +
        @"september|october|november|december|today|tonight|tomorrow|yesterday|(?:next|last|this)\s+(?:week|month|year|time|quarter)|" +
        @"end\s+of\s+(?:the\s+)?(?:day|week|month)|eod|asap|morning|afternoon|evening|noon|midnight|o'?clock|deadline)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DayOrDate();

    /// <summary>A capitalised word that does not start a sentence, a line or a quote.</summary>
    [GeneratedRegex(@"(?<![.!?:]\s|^|\n|[""“(]|\n\s*[-*•]\s|\d[.)]\s)(?<=\s)\p{Lu}[\p{L}\p{N}'’-]*", RegexOptions.Multiline)]
    private static partial Regex MidSentenceCapital();

    /// <summary>The model talking as itself, to the user, rather than writing the user's email.</summary>
    [GeneratedRegex(
        @"\b(?:as\s+an\s+ai|(?:a|an)\s+(?:ai|language\s+model|assistant)\b|my\s+(?:system\s+)?(?:prompt|instructions|programming|guidelines)|" +
        @"i(?:'m|\s+am)\s+(?:unable|not\s+able)\s+to\s+(?:help|assist|reveal|share|comply|do\s+that)|(?:another\s+way|how\s+else|anything(?:\s+else)?)\s+i\s+can\s+(?:help|assist)|" +
        @"here(?:'s|\s+is)\s+(?:a|the|your)\s+(?:joke|email|draft|body))",
        RegexOptions.IgnoreCase)]
    private static partial Regex AssistantVoice();

    /// <summary>Words of the rules themselves, which an email would only hold if the model printed its prompt.</summary>
    [GeneratedRegex(@"<request>|system\s+prompt|my\s+instructions|the\s+request\s+(?:says|asks|gives)|reply\s+with\s+the\s+body", RegexOptions.IgnoreCase)]
    private static partial Regex PromptWords();
}
