namespace VoiceCtrl.Core.Transcription;

/// <summary>The register the model is told to write in, when tone awareness is on: the app's Style
/// (ticket 56), or for AI prompts and code, words and casing kept as said. The Style's capitals,
/// punctuation and formal words are done in code (<see cref="StyleRules"/>), so this is only a hint.
/// Formal keeps each kind's own note from ticket 55 and adds no "formal" wording: told to write
/// formally, the local model rephrases ("Yes, that sounds good") and the guard has to throw the
/// whole cleanup away.</summary>
internal static class ToneHintMapper
{
    private const string ProfessionalHint = "professional tone, complete sentences";
    private const string ProfessionalChatHint = "professional tone";
    private const string DocumentHint = "clear, complete sentences, as written in a document";
    private const string CasualHint = "casual, conversational tone; contractions are fine";
    private const string VeryCasualHint = "very casual, like a text to a friend; contractions are fine";
    private const string ExcitedHint = "upbeat, enthusiastic tone; contractions are fine";
    private const string TechnicalHint =
        "likely a prompt for an AI tool, a code comment or a command; preserve technical terms and casing exactly as spoken";

    public static string? For(AppKind kind, Style? style) => style switch
    {
        null => kind == AppKind.AiPrompt ? TechnicalHint : null,
        Style.Formal => kind switch
        {
            AppKind.Email => ProfessionalHint,
            AppKind.Document => DocumentHint,
            AppKind.WorkChat or AppKind.PersonalChat => ProfessionalChatHint,
            _ => null,
        },
        Style.Casual => CasualHint,
        Style.VeryCasual => VeryCasualHint,
        Style.Excited => ExcitedHint,
        _ => null,
    };
}
