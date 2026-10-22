using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>What the prompt falls back to for an app with no Profile, or a Profile field left unset.</summary>
/// <param name="DefaultCleanup">The Default cleanup from Settings, read at each Dictation.</param>
/// <param name="ToneAwareness">Whether VoiceCtrl may guess a tone from the foreground app.</param>
/// <param name="PressEnter">The Press Enter setting, read at each Dictation.</param>
/// <param name="Styles">The Style screen's settings, read at each Dictation; null is a fresh install's.</param>
internal sealed record PromptDefaults(
    string DefaultCleanup,
    bool ToneAwareness,
    IReadOnlyCollection<string> PromptStyleApps,
    IReadOnlyCollection<string> ChatStyleApps,
    bool PressEnter = false,
    StyleSettings? Styles = null);

/// <summary>Everything about a single request that depends on which application the speaker is
/// dictating into. Resolved once per transcription, since a Profile can override any of it.</summary>
/// <param name="Chat">A chat message's layout (ticket 59): one paragraph in a personal chat, paragraphs by topic in a work chat.</param>
/// <param name="Kind">The app's kind, before any Profile: what the built-in treatment was based on.</param>
/// <param name="Style">The app's Style, after its Profile; null keeps capitals and punctuation as they come.</param>
/// <summary>How a chat message is laid out (ticket 59): like a typed text, never a line a sentence.</summary>
internal enum ChatLayout
{
    /// <summary>Not a chat message.</summary>
    None,

    /// <summary>A personal chat's: one paragraph, greeting included.</summary>
    OneParagraph,

    /// <summary>A work chat's: short paragraphs, a new one where the topic changes.</summary>
    Paragraphs,
}

internal readonly record struct PromptContext(
    string CleanupLevel,
    string? ToneHint,
    string? FormattingHint,
    string? ExtraInstructions,
    bool EmailLayout,
    bool PressEnter = false,
    ChatLayout Chat = ChatLayout.None,
    AppKind Kind = AppKind.Other,
    Style? Style = null);

/// <summary>
/// The Gemini prompt (spec §5.3): the fixed rules, then the Cleanup Level, the user's notes and
/// Dictionary, and whatever the foreground app's Profile asks for. Sent to Gemini with the audio;
/// Offline, <see cref="SmartCleanupPrompt"/> sends the same context sections to the local model as text.
/// </summary>
internal static class GeminiPrompt
{
    private const string Template = """
        You are a dictation transcription engine. Listen to the attached audio clip and produce the
        final cleaned-up text the speaker intended to write.

        1. Transcribe accurately first, then silently clean up. Do not show the raw transcript or your work.
        2. {0}
        3. If the speaker corrects or retracts something mid-utterance (e.g. "send it to John...
           actually, no, make that Sarah", "the meeting is at 3, wait, 4 PM"), output only the
           final corrected version. Drop the retracted content entirely. Do not show both.
        4. Preserve the speaker's actual meaning and wording. Do not summarize or add information.
        5. Apply explicit formatting instructions ("new paragraph", "bullet point") instead of
           transcribing them literally. "New line", "next line" and "line break" start a new line,
           "new paragraph" and "skip a line" leave a blank line, unless the words are plainly part of
           the sentence ("the next line of the file"). If the speaker enumerates two or more items
           with clear verbal markers ("first... second...", "one, two, three", or a count followed
           by numbered items: "three things, one X, two Y, three Z") without explicitly asking for
           a list, format them as one anyway, in any application. The list is the speaker's own
           structure, so an application hint about tone or style never removes it.
        6. Transcribe in the same language the speaker used.
        7. If the audio is silent or has no discernible speech, respond with exactly: [NO_SPEECH_DETECTED]
        8. Output ONLY the final text, plus the block in rule 9 when it applies. No preamble, no
           quotes, no markdown fences, no "Here is...".
        9. If the final text differs from what was said by more than punctuation and capital
           letters (a filler removed, a correction applied, a list laid out), end your answer with
           a line holding exactly [ORIGINAL_TRANSCRIPT] and then, unchanged, what was said. Otherwise
           add nothing after the final text.
        """;

    // Structural only (CONTEXT.md, Email Layout): the model moves the words it heard into an
    // email's shape and never writes a greeting, sign-off or name of its own.
    private const string EmailLayoutInstruction =
        "Email Layout: this text is an email, so lay it out as one. Put the greeting on its own line, " +
        "the body in short paragraphs, and the sign-off on its own line, with a blank line between " +
        "each. This is layout only: never add a word the speaker did not say. Include a greeting or " +
        "a sign-off only if the speaker said one, and a name only if the speaker said it. Where this " +
        "conflicts with the formatting hint, this wins. Rule 4 still takes priority over everything here.";

    // Rule 5 would otherwise turn a final "press enter" into a line break, and cleanup could drop
    // it; PressEnterDetector needs the words to find them.
    private const string PressEnterInstruction =
        "Press Enter: if the speaker's last words are \"press enter\", end the text with the words " +
        "\"press enter\" exactly as spoken. Do not turn them into a line break or leave them out. " +
        "Anywhere else, treat those words as ordinary speech.";

    /// <summary>
    /// Layers the app's Profile over the advanced-settings lists and then the app kind's built-in
    /// treatment. A field the Profile does not set falls through rather than blanking, so
    /// overriding one aspect of an app never silently discards the rest.
    /// </summary>
    /// <param name="windowTitle">The foreground window's title, which tells a browser's sites apart.
    /// Never logged: it can hold an email's subject.</param>
    public static PromptContext ContextFor(AppProfile? profile, string? processName, PromptDefaults defaults, string? windowTitle = null)
    {
        StyleSettings styles = defaults.Styles ?? new StyleSettings();
        AppKind kind = AppKinds.Of(processName, windowTitle, defaults.PromptStyleApps, defaults.ChatStyleApps, styles.AppKinds);
        string? explicitTone = NullIfBlank(profile?.Tone);
        string? explicitFormatting = NullIfBlank(profile?.Formatting);

        // An app's Profile wins over its kind's Style (ticket 56). AI prompts and code have none, so
        // their words and capitals stay as said unless the user gave that one app a Style.
        Style? style = StyleNames.Parse(profile?.Style) ?? styles.For(kind);

        // A tone stated in a profile is the user speaking directly, so it applies even when
        // ENABLE_TONE_AWARENESS is off. That flag governs VoiceCtrl guessing a tone from the
        // foreground app, which is a different thing from the user writing one down.
        string? toneHint = explicitTone switch
        {
            null => defaults.ToneAwareness ? ToneHintMapper.For(kind, style) : null,
            _ when IsNone(explicitTone) => null,
            _ => explicitTone,
        };

        string? formattingHint = explicitFormatting switch
        {
            null => FormattingHintMapper.For(kind),
            _ when IsNone(explicitFormatting) => null,
            // An unrecognised value is treated as a typo rather than as an instruction to suppress
            // formatting, so a misspelled profile degrades to the built-in behaviour.
            _ => FormattingHintMapper.ResolveExplicit(explicitFormatting, kind) ?? FormattingHintMapper.For(kind),
        };

        return new PromptContext(
            ProfileCleanup(profile) ?? defaults.DefaultCleanup,
            toneHint,
            formattingHint,
            NullIfBlank(profile?.Instructions),
            profile?.EmailLayout ?? kind == AppKind.Email,
            defaults.PressEnter,
            FormattingHintMapper.ChatLayoutOf(formattingHint),
            kind,
            style);
    }

    public static string Build(PromptContext context, string styleNotes, IReadOnlyList<DictionaryEntry> dictionary) =>
        string.Format(Template, CleanupLevelMapper.Resolve(context.CleanupLevel)) +
        ContextSections(context, styleNotes, dictionary);

    /// <summary>Everything after the fixed rules: the user's notes, Dictionary, the app's hints and
    /// the layout rules. Shared with the local text prompt, so a Profile means the same thing in
    /// both modes.</summary>
    internal static string ContextSections(PromptContext context, string styleNotes, IReadOnlyList<DictionaryEntry> dictionary)
    {
        string prompt = string.Empty;

        if (!string.IsNullOrWhiteSpace(styleNotes))
        {
            prompt += "\n\nAdditional user preferences. Rule 4 (preserve meaning/wording) still " +
                      $"takes priority if these ever conflict with it:\n{styleNotes.Trim()}";
        }

        if (dictionary.Count > 0)
        {
            // Framed as spelling rather than as vocabulary on purpose. The failure mode worth
            // guarding against is the model reaching for a listed term because it appears in the
            // prompt, when the speaker said something else entirely.
            prompt += "\n\nSpelling reference. These are names and terms this speaker uses. When " +
                      "something in the audio clearly matches one of these, spell it exactly as " +
                      "written here. Never insert one that was not actually said:\n- " +
                      string.Join("\n- ", dictionary.Select(FormatSpellingLine));
        }

        if (!string.IsNullOrWhiteSpace(context.ToneHint))
        {
            prompt += "\n\nContext hint (best-effort, from the active application, ignore if it " +
                      $"doesn't fit what was actually said): {context.ToneHint}";
        }

        if (!string.IsNullOrWhiteSpace(context.FormattingHint))
        {
            prompt += "\n\nFormatting hint (best-effort, from the active application, ignore if " +
                      $"it doesn't fit what was actually said): {context.FormattingHint}";
        }

        if (context.EmailLayout)
        {
            prompt += "\n\n" + EmailLayoutInstruction;
        }

        if (!string.IsNullOrWhiteSpace(context.ExtraInstructions))
        {
            prompt += "\n\nInstructions for this specific application, from the user's profile for " +
                      $"it. Rule 4 still takes priority if these conflict with it: {context.ExtraInstructions}";
        }

        // Last, so no Profile instruction or layout rule comes after it and reshapes the ending.
        if (context.PressEnter)
        {
            prompt += "\n\n" + PressEnterInstruction;
        }

        return prompt;
    }

    /// <summary>The Profile's own Cleanup Level, or null for Default. A value that isn't one of the
    /// stored names (a typo in a hand-edited file) also means Default, not "standard".</summary>
    private static string? ProfileCleanup(AppProfile? profile)
    {
        string? level = NullIfBlank(profile?.Cleanup)?.ToLowerInvariant();
        return CleanupLevels.IsValid(level) ? level : null;
    }

    /// <summary>"Bluepeak", or "Bluepeak (often heard as: blue peak, blu peak)".</summary>
    private static string FormatSpellingLine(DictionaryEntry entry) =>
        entry.AlsoHeardAs.Count == 0 ? entry.Word : $"{entry.Word} (often heard as: {string.Join(", ", entry.AlsoHeardAs)})";

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsNone(string value) =>
        string.Equals(value, AppProfile.None, StringComparison.OrdinalIgnoreCase);
}
