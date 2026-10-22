using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// The last step before a Transcription is pasted (ticket 55): the parts of each app kind's layout
/// that a rule settles for certain, done in code on either engine's text so Online and Offline agree.
/// It moves line breaks and punctuation; the only words it takes out are a spelled-out list's
/// "first ... and second" (now its numbers) and an opening "So," or "Okay so" (not Word for word).
/// An email's greeting and sign-off go on their own lines, a list is set off from the text around
/// it, a chat message reads like a typed text (ticket 59), and no Markdown hard break (two trailing spaces)
/// reaches the app. Then the app's Style (<see cref="StyleRules"/>).
/// </summary>
internal static partial class PasteLayout
{
    /// <summary>A lead-in this short ("It should:") reads better on its sentence's line.</summary>
    private const int MinOwnLineLeadInWords = 4;

    /// <param name="isOrdinaryWord">For Very casual: whether a lower-case word is ordinary English,
    /// not a name, so it may start a sentence in lower case. Null keeps every capital.</param>
    public static string Finish(string text, PromptContext context, Func<string, bool>? isOrdinaryWord = null)
    {
        string laidOut = Tidy(text);
        if (laidOut.Length == 0)
        {
            return laidOut;
        }

        if (context.CleanupLevel is CleanupLevels.Standard or CleanupLevels.Aggressive)
        {
            laidOut = WithoutOpeningFiller(laidOut);
        }

        laidOut = SpokenLists.Format(laidOut);
        laidOut = LeadInsOnTheirOwnLines(laidOut);
        if (context.EmailLayout)
        {
            laidOut = EmailParts(laidOut);
        }

        if (context.EmailLayout || FormattingHintMapper.IsParagraphs(context.FormattingHint))
        {
            laidOut = ListsSetOff(laidOut);
        }

        if (context.Chat != ChatLayout.None)
        {
            laidOut = ChatParagraphs(laidOut, context.Chat, isOrdinaryWord);
        }

        // Last, on the finished layout: the Style changes capitals, closing punctuation and register.
        return context.Style is { } style ? StyleRules.Apply(laidOut, style, context.CleanupLevel, isOrdinaryWord) : laidOut;
    }

    /// <summary>A chat message reads like a typed text (ticket 59, owner): a personal chat's is one
    /// paragraph, a work chat's keeps the breaks between its topics. A line the model broke off is
    /// joined back into its paragraph; a list keeps its lines.</summary>
    private static string ChatParagraphs(string text, ChatLayout layout, Func<string, bool>? isOrdinaryWord)
    {
        string blocks = layout == ChatLayout.OneParagraph ? BlankLines().Replace(text, "\n") : text;
        return string.Join("\n\n", blocks.Split("\n\n").Select(block =>
        {
            List<string> lines = [.. block.Split('\n')];
            for (int i = lines.Count - 2; i >= 0; i--)
            {
                if (!IsListItem(lines[i]) && !IsListItem(lines[i + 1]) && lines[i].Trim().Length > 0)
                {
                    lines[i] = JoinLines(lines[i], lines[i + 1], isOrdinaryWord);
                    lines.RemoveAt(i + 1);
                }
            }

            return string.Join('\n', lines);
        }));
    }

    /// <summary>Two lines as one sentence run: after a full stop as they are; after a comma or a
    /// greeting line ("Hey Emma") the next line went on the same sentence, so its capital was only
    /// the line's; any other line ended a sentence the model left unpunctuated.</summary>
    private static string JoinLines(string previous, string next, Func<string, bool>? isOrdinaryWord)
    {
        previous = previous.TrimEnd();
        next = next.TrimStart();
        if (next.Length == 0)
        {
            return previous;
        }

        char last = previous[^1];
        return last is '.' or '!' or '?' or ':' or ';' or '…' ? previous + " " + next
            : last == ',' ? previous + " " + Continuing(next, isOrdinaryWord)
            : GreetingLine().IsMatch(previous) ? previous + ", " + Continuing(next, isOrdinaryWord)
            : previous + ". " + next;
    }

    /// <summary>The next line's first word in lower case when it is an ordinary word; "I" and names keep
    /// their capitals, and without the word list every capital stays.</summary>
    private static string Continuing(string next, Func<string, bool>? isOrdinaryWord)
    {
        string word = FirstWord().Match(next).Value;
        bool keep = isOrdinaryWord is null || word.Length == 0 || word == "I" || word.StartsWith("I'", StringComparison.Ordinal) ||
                    word.Skip(1).Any(char.IsUpper) || !isOrdinaryWord(word.ToLowerInvariant());
        return keep ? next : char.ToLowerInvariant(next[0]) + next[1..];
    }

    private static string Tidy(string text)
    {
        string lines = string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd()));
        return ExtraBlankLines().Replace(lines, "\n\n").Trim();
    }

    /// <summary>"The build is done. Two things:" above a list becomes two lines, so the list's
    /// lead-in sits right above it.</summary>
    private static string LeadInsOnTheirOwnLines(string text)
    {
        List<string> lines = [.. text.Split('\n')];
        for (int i = lines.Count - 2; i >= 0; i--)
        {
            if (!IsListItem(lines[i]) && IsListItem(lines[i + 1]) && lines[i].EndsWith(':') &&
                LastSentenceStart().Match(lines[i]) is { Success: true } boundary &&
                lines[i][boundary.Index..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinOwnLineLeadInWords)
            {
                lines[i] = lines[i][..boundary.Index].TrimEnd() + "\n" + lines[i][boundary.Index..];
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>A blank line before a list (or its lead-in) and after it, as in an email or a document.</summary>
    private static string ListsSetOff(string text)
    {
        List<string> lines = [.. text.Split('\n')];
        for (int i = 1; i < lines.Count; i++)
        {
            bool startsList = IsListItem(lines[i]) && !IsListItem(lines[i - 1]);
            int blockStart = startsList && lines[i - 1].EndsWith(':') ? i - 1 : i;
            if (startsList && blockStart > 0 && lines[blockStart - 1].Length > 0)
            {
                lines.Insert(blockStart, string.Empty);
                i++;
            }

            bool endsList = IsListItem(lines[i - 1]) && !IsListItem(lines[i]);
            if (endsList && lines[i].Length > 0)
            {
                lines.Insert(i, string.Empty);
                i++;
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>The greeting and the sign-off on their own lines, the sender's name under the sign-off.
    /// Only words the speaker said: an email with neither is left as it is.</summary>
    private static string EmailParts(string text)
    {
        string? greeting = null;
        string body = text;

        if (Greeting().Match(body) is { Success: true } hello)
        {
            greeting = hello.Groups["words"].Value + (hello.Groups["mark"].Value == "!" ? "!" : ",");
            body = Capitalise(hello.Groups["rest"].Value);
        }

        string? signOff = null;
        if (SignOff().Match(body) is { Success: true } bye && bye.Index > 0)
        {
            string name = bye.Groups["name"].Value;
            signOff = name.Length > 0
                ? bye.Groups["words"].Value + ",\n" + name
                : bye.Groups["words"].Value + bye.Groups["mark"].Value;
            body = body[..bye.Index].TrimEnd();
        }

        if (body.Length == 0)
        {
            return text;
        }

        body = ParagraphsOfLines(body);

        return string.Join("\n\n", new[] { greeting, body, signOff }.Where(part => part is not null));
    }

    /// <summary>An email's paragraphs are blocks between blank lines; a body written a sentence a line
    /// is joined back into its paragraph. A list and the lead-in above it keep their lines.</summary>
    private static string ParagraphsOfLines(string body) =>
        string.Join("\n\n", body.Split("\n\n").Select(block =>
        {
            List<string> lines = [.. block.Split('\n')];
            for (int i = lines.Count - 2; i >= 0; i--)
            {
                bool leadIn = i + 2 < lines.Count && IsListItem(lines[i + 2]);
                if (!IsListItem(lines[i]) && !IsListItem(lines[i + 1]) && !leadIn)
                {
                    lines[i] += " " + lines[i + 1];
                    lines.RemoveAt(i + 1);
                }
            }

            return string.Join('\n', lines);
        }));

    /// <summary>"So," "Well," "Okay so" opening the text: a filler, not the first word of the message.
    /// "So far" and "So the plan is" stay, since without the comma "so" can belong to the sentence.</summary>
    private static string WithoutOpeningFiller(string text)
    {
        Match filler = OpeningFiller().Match(text);
        string rest = text[filler.Length..];
        return filler.Success && rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2 ? Capitalise(rest) : text;
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    [GeneratedRegex(@"^(?:(?:okay|ok|alright|well|so),\s+|(?:okay|ok|alright),?\s+so,?\s+)+", RegexOptions.IgnoreCase)]
    private static partial Regex OpeningFiller();

    private static bool IsListItem(string line) => ListItem().IsMatch(line);

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^[\p{L}']+")]
    private static partial Regex FirstWord();

    /// <summary>A line that is only a greeting: "Hey Emma", "Hi all", "Hello".</summary>
    [GeneratedRegex(@"^(?i:hi|hello|hey|hiya|dear|morning|good (?:morning|afternoon|evening))(?:\s+[\p{L}'.-]+){0,2}$")]
    private static partial Regex GreetingLine();

    [GeneratedRegex(@"^\s*(?:\d+[.)]|[-*•])\s")]
    private static partial Regex ListItem();

    /// <summary>Where the last sentence of a line starts, when the line holds more than one.</summary>
    [GeneratedRegex(@"(?<=[.!?]\s+)(?=[^.!?]+$)")]
    private static partial Regex LastSentenceStart();

    /// <summary>"Hi Sarah," "Dear Mr Lee," "Hello team," "Good morning Priya." at the very start,
    /// then the rest. Only capitalised names or a few group words count as who is greeted, so
    /// "Hello world is the first program" is not a greeting.</summary>
    [GeneratedRegex(
        @"^(?<words>(?i:hi|hello|hey|hiya|dear|greetings|good (?:morning|afternoon|evening))" +
        @"(?:\s+(?:\p{Lu}[\p{L}'.-]*|(?i:all|team|everyone|there|guys|folks|both|again)))*)" +
        @"(?<mark>[,!.:])\s*(?<rest>\S[\s\S]*)$")]
    private static partial Regex Greeting();

    /// <summary>"Thanks, Alex." "Best regards, Priya" "Cheers!" "Thank you." at the very end, after a
    /// sentence or a line. A sign-off is followed only by the sender's capitalised name.</summary>
    [GeneratedRegex(
        @"(?<=(?:^|[.!?:]\s+|\n))(?<words>(?i:thanks(?: again| so much)?|thank you(?: so much)?|many thanks|cheers|" +
        @"best|best wishes|best regards|kind regards|warm regards|regards|all the best|sincerely|yours sincerely|" +
        @"yours truly|take care|talk soon|speak soon))(?<mark>[,.!]?)" +
        @"(?:\s+(?<name>\p{Lu}[\p{L}'-]*(?:\s\p{Lu}[\p{L}'-]*){0,2}))?[.!]?\s*$")]
    private static partial Regex SignOff();
}
