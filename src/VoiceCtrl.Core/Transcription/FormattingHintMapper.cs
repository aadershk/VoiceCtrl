using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>The structure a Transcription takes in each <see cref="AppKind"/> (ticket 55). Email has
/// none of its own here: its layout is Email Layout.</summary>
internal static class FormattingHintMapper
{
    private const string StructuredHint =
        "this text is likely a prompt or instruction for an AI tool or a terminal. If the speaker's " +
        "content has enumerable structure (steps, multiple distinct items or options), format it with " +
        "Markdown \"-\" bullets or \"1.\"/\"2.\" numbered steps, even for just two items, keeping any words said before the first item as a line above the list. Formatting only. " +
        "never change or add words.";

    // A chat message reads like a typed text (ticket 59, owner): a personal one is one paragraph, a
    // work one is short paragraphs by topic. Neither puts a sentence a line.
    private const string ChatListRule =
        "A list the speaker spelled out, with numbers (\"one... two... three...\"), ordinals " +
        "(\"first... second...\") or \"bullet point\", or announced (\"three things:\"), is still a " +
        "numbered or \"-\" list, since that is the structure they asked for. Without such cues, avoid " +
        "Markdown bullet or numbered-list syntax.";

    // "The way a person types a text" made the local model drop capitals and punctuation, so the
    // hint asks for normal sentences in plain words.
    private const string PersonalChatHint =
        "this text is a message in a personal chat, so keep it as one paragraph: normal sentences with their " +
        "usual capitals and punctuation, a greeting included, and no line break between sentences. " + ChatListRule;

    private const string WorkChatHint =
        "this text is a message in a work chat, so write normal sentences in short paragraphs: a short " +
        "message is one paragraph, and in a longer one start a new paragraph (a blank line) where the topic " +
        "changes, never a new line for each sentence. Use plain commas and full stops, not dashes. " + ChatListRule;

    private const string ParagraphsHint =
        "this text is going into a document, so write full sentences in paragraphs. In a longer text, " +
        "start a new paragraph (a blank line) where the topic changes. When the speaker announces a " +
        "heading or title (\"heading project goals\"), put the heading on its own line, without the word " +
        "\"heading\" or \"title\", followed by a blank line. Formatting only: never change or add words.";

    /// <summary>
    /// Maps a profiles.json "formatting" value onto the hint text. Returns null both for "none",
    /// which means the user asked for no formatting hint at all, and for an unrecognised value,
    /// where guessing at a typo would be worse than falling through to the built-in kind.
    /// </summary>
    /// <param name="kind">The app's kind: a work chat's Prose is paragraphs by topic, any other app's one paragraph.</param>
    public static string? ResolveExplicit(string? formatting, AppKind kind = AppKind.PersonalChat) => formatting?.Trim().ToLowerInvariant() switch
    {
        AppProfile.Structured => StructuredHint,
        AppProfile.Prose => kind == AppKind.WorkChat ? WorkChatHint : PersonalChatHint,
        AppProfile.Paragraphs => ParagraphsHint,
        _ => null,
    };

    /// <summary>What the app's kind gives it, as a Profile's stored Formatting value: the Profile
    /// Editor's starting choice, so a new Profile changes nothing until the user picks differently.</summary>
    public static string BuiltIn(string processName) => AppKinds.Of(processName) switch
    {
        AppKind.AiPrompt => AppProfile.Structured,
        AppKind.WorkChat or AppKind.PersonalChat => AppProfile.Prose,
        AppKind.Document => AppProfile.Paragraphs,
        _ => AppProfile.None,
    };

    /// <summary>Whether <paramref name="formattingHint"/> is a chat message's hint.</summary>
    public static bool IsProse(string? formattingHint) => ChatLayoutOf(formattingHint) != ChatLayout.None;

    /// <summary>The chat layout <paramref name="formattingHint"/> asks for, or None.</summary>
    public static ChatLayout ChatLayoutOf(string? formattingHint) =>
        formattingHint == PersonalChatHint ? ChatLayout.OneParagraph
        : formattingHint == WorkChatHint ? ChatLayout.Paragraphs
        : ChatLayout.None;

    /// <summary>Whether <paramref name="formattingHint"/> is the AI prompt and code hint.</summary>
    public static bool IsStructured(string? formattingHint) => formattingHint == StructuredHint;

    /// <summary>Whether <paramref name="formattingHint"/> is the document hint.</summary>
    public static bool IsParagraphs(string? formattingHint) => formattingHint == ParagraphsHint;

    /// <summary>The hint for an app with no Profile of its own.</summary>
    public static string? For(AppKind kind) => kind switch
    {
        AppKind.AiPrompt => StructuredHint,
        AppKind.PersonalChat => PersonalChatHint,
        AppKind.WorkChat => WorkChatHint,
        AppKind.Document => ParagraphsHint,
        _ => null,
    };

    // extraStructuredApps/extraProseApps let the advanced settings' prompt/chat style app lists extend or
    // correct the built-in kinds without a code change. They're checked first, so a user override
    // always wins over a kind assigned in AppKinds.
    public static string? Resolve(
        string? processName,
        IReadOnlyCollection<string>? extraStructuredApps = null,
        IReadOnlyCollection<string>? extraProseApps = null,
        string? windowTitle = null) =>
        For(AppKinds.Of(processName, windowTitle, extraStructuredApps, extraProseApps));
}
