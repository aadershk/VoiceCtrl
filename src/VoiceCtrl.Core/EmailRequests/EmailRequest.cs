using System.Text.RegularExpressions;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.EmailRequests;

/// <summary>
/// A spoken request for an email (CONTEXT.md, Email request; ticket 57): "write an email to Mark
/// thanking him for his time", said while holding the Rewrite key with nothing selected in an email
/// app. VoiceCtrl writes the whole email from it. Plain dictation is never read for requests.
/// </summary>
/// <param name="Words">The request as the speech engine wrote it down, on one line.</param>
/// <param name="Recipient">Who the email is to, only as the request names them ("Mark", "Anna and
/// Tom", "Dr. Patel"), or a group word ("team", "everyone", "all"); null when it names no one.</param>
public sealed partial record EmailRequest(string Words, string? Recipient)
{
    /// <summary>The email app's Style, which sets the greeting, the sign-off and the register.</summary>
    public Style Style { get; init; } = Style.Formal;

    /// <summary>The Style screen's "Your name", signed under the sign-off; empty signs no name.</summary>
    public string YourName { get; init; } = string.Empty;
}

/// <summary>
/// Tells, from a Rewrite key instruction said with nothing selected, whether it asks for a new email
/// ("write to Oscar and ask if he's free", "send my landlord a note") or an edit of the last Dictation
/// ("make it shorter", "translate this to Dutch"). Both are commands, since the key was held, so a
/// wrong guess never touches dictated words. An instruction that is neither clearly a request nor
/// clearly an edit stays an edit, which is what the Rewrite key did before.
/// </summary>
public sealed partial record EmailRequest
{
    /// <summary>The request in <paramref name="instruction"/>, or null when it is an edit (or empty).</summary>
    public static EmailRequest? FromInstruction(string instruction)
    {
        string words = Whitespace().Replace(instruction, " ").Trim();
        if (words.Length == 0)
        {
            return null;
        }

        // An order to write an email wins ("Put together an email to the team"); then anything that
        // reads as an edit; then any other opening that asks for something to be written.
        Match order = Order().Match(words);
        if (!order.Success && (PointsAtTheText().IsMatch(words) || !WritingOpening().IsMatch(words)))
        {
            return null;
        }

        // "To Sam, no wait, to Alex": the last correction names who it is to.
        Match corrected = Correction().Matches(words) is { Count: > 0 } corrections ? corrections[^1] : Match.Empty;
        string? named = corrected.Success ? corrected.Groups["to"].Value
            : order.Groups["to"].Success ? order.Groups["to"].Value
            : order.Groups["object"].Success ? order.Groups["object"].Value
            : Addressee().Match(words) is { Success: true } addressee ? addressee.Groups["to"].Value
            : null;
        return new EmailRequest(words, Greeted(named));
    }

    private static string? Greeted(string? named)
    {
        if (named is null)
        {
            return null;
        }

        named = named.Trim().TrimEnd(',', '.');
        if (IsRole().IsMatch(named))
        {
            return null;
        }

        return Group().Match(named) is { Success: true } group
            ? group.Groups["word"].Value.ToLowerInvariant() switch
            {
                "team" => "team",
                "everyone" or "everybody" => "everyone",
                _ => "all",
            }
            : named;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // A name as the request says it: an optional title, a capitalised first name, an optional
    // capitalised surname. Several are joined by "and" or commas.
    // Case-sensitive even inside a case-insensitive pattern: the capital is what makes it a name.
    // A possessive is never the name ("a reply to Sarah's question"), so each word is taken whole.
    // A day or a month is never a name: "a reminder to Aisha, or no, make that Wednesday".
    private const string Word =
        @"(?!(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|January|February|March|April|May|June|July|August|" +
        @"September|October|November|December)\b)(?>\p{Lu}[\p{L}'’-]*)(?<!['’]s)";
    // "I" is never a surname: "Tell Priya I'll be late".
    private const string Name = @"(?-i:(?:(?:Dr|Mr|Mrs|Ms|Mx|Prof|Professor)\.?\s+)?" + Word + @"(?:\s+(?!I\b|I['’])" + Word + ")?)";
    private const string Names = Name + @"(?:(?:,\s*|,?\s+and\s+)" + Name + ")*";
    private const string GroupWords =
        @"(?:the\s+)?(?:whole\s+|entire\s+)?(?:[\p{L}-]+\s+)?(?:team|board|group|class|committee|staff|department)|everyone|everybody|all";

    // Someone known by a role, not a name: greeted "Hello," since the request gives no name.
    private const string Role =
        @"(?:my|the|our)\s+(?:[\p{L}-]+\s+){0,2}?(?:landlord|landlady|manager|boss|client|customer|vendor|supplier|airline|bank|school|" +
        @"teacher|professor|tutor|doctor|dentist|recruiter|interviewer|neighbou?rs?|investors?|tenants?|agent|accountant|lawyer|" +
        @"insurer|council|coach|parents?|hr|organi[sz]ers?|hosts?|seller|buyer|contractor|plumber|builder|mentor|editor|publisher|" +
        @"hotel|stakeholders?|panel|landlords?)";
    private const string AnyRecipient = "(?:" + Names + "|" + GroupWords + "|" + Role + ")";

    // What is written: an email, a reply, a note, a thank-you, with the words people put before it.
    private const string Written =
        @"(?:(?:quick|short|brief|polite|formal|friendly|nice|warm|kind|professional|casual|gentle|firm|thank[\s-]?you|follow[\s-]?up|update|courteous|short\s+and\s+sweet)\s+)*" +
        @"(?:e-?mail|mail|note|reply|response|message|letter|thank[\s-]?you|follow[\s-]?up|complaint|update|apology|invitation|invite|reminder|something)";

    // What a request goes on to say the email does: "thanking him", "to say", "and ask", "about".
    private const string Purpose = @"(?:[\p{L}]+ing|to\s+[\p{L}]+|and\s+[\p{L}]+|that|about|with|for)\b";

    // A filler or polite lead before the instruction: "Okay,", "Can you", "Please", "Help me".
    private const string Lead =
        @"^(?:(?:okay|ok|so|alright|right|um|uh|hey|now)\b[,.]?\s+)*" +
        @"(?:(?:can|could|would|will)\s+you\s+(?:please\s+)?|please\s+|i\s+need\s+you\s+to\s+|i\s+want\s+you\s+to\s+|" +
        @"i(?:'?d|\s+would)\s+like\s+you\s+to\s+|help\s+me\s+|let(?:'?s|\s+us)\s+|go\s+ahead\s+and\s+)?";

    /// <summary>
    /// The order that opens most requests, which also says who it is to: a verb of writing, then an
    /// email, a reply or a note; "I need an email to Dr. Reyes"; "reply to Ben saying ..."; "email
    /// David and ask ...".
    /// </summary>
    [GeneratedRegex(
        Lead +
        @"(?:" +
            @"(?:(?:write(?:\s+up)?|draft|compose|prepare|create|type(?:\s+up)?|put\s+together|whip\s+up)\s+(?:me\s+)?|" +
            @"(?:send|shoot|fire\s+off|drop)\s+)" +
            @"(?:(?<object>" + Names + "|" + GroupWords + "|" + Role + @")\s+)?" +
            @"(?:(?:a|an|the|my|our)\s+)?" + Written +
            @"(?:\s+(?:to|for|back\s+to|replying\s+to)\s+(?<to>" + AnyRecipient + @"))?" +
            @"(?=$|[\s,.!?])" +
        @"|" +
            @"(?:i\s+need|i(?:'?d|\s+would)\s+like|i\s+want|can\s+i\s+(?:get|have))\s+(?:to\s+(?:send|write|draft)\s+)?(?:a|an)\s+" + Written +
            @"\s+(?:to|for)\s+(?<to>" + AnyRecipient + @")(?=$|[\s,.!?])" +
        @"|" +
            @"(?:reply|respond|answer|write\s+back)\s+to\s+(?:(?<to>" + Names + "|" + Role + @")|(?:this|that|the|his|her|their)\s+(?:e-?mail|message|thread|note))" +
            @"(?:\s+(?:politely|nicely|briefly|quickly|formally|kindly))?\s*[,.]?\s+" + Purpose +
        @"|" +
            @"e-?mail\s+(?<to>" + Names + "|" + Role + @")\s*,?\s+" + Purpose +
        @")",
        RegexOptions.IgnoreCase)]
    private static partial Regex Order();

    /// <summary>Any other opening that asks for something to be written or said to someone: "Write to
    /// Oscar", "Answer Dave's question", "Tell Priya I'm late", "Follow up with Greg", "Something to
    /// Nadia", "A thank-you note to the Hendersons would be great", "Reply saying sounds good".</summary>
    [GeneratedRegex(
        Lead +
        @"(?:(?:i\s+(?:need|want|have)\s+to|i(?:'?d|\s+would)\s+like\s+to)\s+(?:e-?mail|write\s+to|reply\s+to|send|answer|message|tell|ask|thank|let|invite|remind)\b|" +
        @"(?:write|draft|compose|send|shoot|reply|respond|answer|e-?mail|message|tell|ask|thank|invite|remind|follow\s+up|congratulate|apologi[sz]e\s+to|" +
        @"decline|accept|wish|welcome|introduce|let\s+(?:\S+\s+){1,4}?know|say\s+(?:no|yes|thanks|thank\s+you|sorry|congrats|hello|hi)\s+to)\b|" +
        @"(?:a|an)\s+" + Written + @"\b|" + Written + @"\s+(?:to|for)\b|something\s+(?:to|for)\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex WritingOpening();

    /// <summary>An edit of a text that is already there: an edit verb at the start ("make it shorter",
    /// "translate to Dutch", "cut this down", "list the points"), a bare comparison ("shorter please"),
    /// or the instruction pointing at the text ("write this more formally", "say this in Spanish").</summary>
    [GeneratedRegex(
        Lead +
        @"(?:(?:make|turn|change|rewrite|reword|rephrase|shorten|cut|trim|lengthen|expand|fix|correct|translate|put|list|summari[sz]e|" +
        @"simplify|format|convert|bullet|number|capitali[sz]e|tidy|clean|polish|tighten|soften|add|remove|delete|replace|undo|redo|edit|" +
        @"proofread|spell[\s-]?check)\b|(?:shorter|longer|more\s+\w+|less\s+\w+|friendlier|punchier|simpler|clearer)\b|" +
        @"\w+\s+(?:this|it|that|the\s+(?:text|last\s+(?:one|bit|paragraph|sentence)|whole\s+thing))\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex PointsAtTheText();

    /// <summary>Who it is to when no opening order said it: "to Oscar", "tell Priya", "answer Dave's",
    /// "follow up with Greg".</summary>
    [GeneratedRegex(
        @"\b(?:to|tell|ask|thank|invite|remind|congratulate|answer|message|e-?mail|with)\s+(?<to>" + Names + "|" + Role + @")|\blet\s+(?<to>" + Names + @")\s+know\b|(?<to>(?-i:\p{Lu}\p{L}+))['’]s\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Addressee();

    [GeneratedRegex(@"^(?:the\s+)?(?:whole\s+|entire\s+)?(?:[\p{L}-]+\s+)?(?<word>team|board|group|class|committee|staff|department)$|^(?<word>everyone|everybody|all)$", RegexOptions.IgnoreCase)]
    private static partial Regex Group();

    [GeneratedRegex(@"^" + Role + "$", RegexOptions.IgnoreCase)]
    private static partial Regex IsRole();

    /// <summary>The speaker correcting who it is to: "no wait, to Alex", "actually make that a note to
    /// Maria and Joe", "actually, no, make it to Rachel".</summary>
    [GeneratedRegex(
        @"\b(?:no|wait|actually|sorry)\b[,.]?\s+(?:(?:no|wait|actually|sorry)\b[,.]?\s+)*" +
        @"(?:(?:make|change)\s+(?:it|that)\s+(?:(?:a|an)\s+" + Written + @"\s+)?(?:(?:to|for)\s+)?|(?:to|for)\s+)(?<to>" + AnyRecipient + ")",
        RegexOptions.IgnoreCase)]
    private static partial Regex Correction();
}
