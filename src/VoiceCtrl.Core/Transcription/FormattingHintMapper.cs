using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

internal static class FormattingHintMapper
{
    private const string StructuredHint =
        "this text is likely a prompt or instruction for an AI tool or a terminal. If the speaker's " +
        "content has enumerable structure (steps, multiple distinct items or options), format it with " +
        "Markdown \"-\" bullets or \"1.\"/\"2.\" numbered steps, even for just two items. Formatting only. " +
        "never change or add words.";

    private const string ProseHint =
        "this text is likely a casual chat message, so write it as continuous natural sentences, the way " +
        "people type chat messages, even if multiple points are mentioned. Avoid Markdown bullet or " +
        "numbered-list syntax.";

    // "claude" (Claude Code CLI) is a console app with no window of its own, so GetForegroundWindow
    // actually resolves to whatever terminal is hosting it. The entries below cover the common
    // Windows terminal/shell hosts so structured formatting still applies regardless of which one
    // the user runs Claude Code (or another CLI/AI tool) in.
    private static readonly Dictionary<string, string> ProcessNameToHint = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windowsterminal"] = StructuredHint,
        ["code"] = StructuredHint,
        ["cursor"] = StructuredHint,
        ["devenv"] = StructuredHint,
        ["claude"] = StructuredHint,
        ["powershell"] = StructuredHint,
        ["pwsh"] = StructuredHint,
        ["cmd"] = StructuredHint,
        ["conhost"] = StructuredHint,
        ["mintty"] = StructuredHint,
        ["slack"] = ProseHint,
        ["discord"] = ProseHint,
        ["whatsapp"] = ProseHint,
    };

    /// <summary>
    /// Maps a profiles.json "formatting" value onto the hint text. Returns null both for "none",
    /// which means the user asked for no formatting hint at all, and for an unrecognised value,
    /// where guessing at a typo would be worse than falling through to the built-in table.
    /// </summary>
    public static string? ResolveExplicit(string? formatting) => formatting?.Trim().ToLowerInvariant() switch
    {
        AppProfile.Structured => StructuredHint,
        AppProfile.Prose => ProseHint,
        _ => null,
    };

    // extraStructuredApps/extraProseApps let PROMPT_STYLE_APPS/CHAT_STYLE_APPS (.env) extend or
    // correct the hardcoded defaults above without a code change, for example if "claude" turns out to be
    // the wrong process name for a given install. They're checked before the built-in table so a
    // user override always wins over a bucket assignment made here.
    public static string? Resolve(
        string? processName,
        IReadOnlyCollection<string>? extraStructuredApps = null,
        IReadOnlyCollection<string>? extraProseApps = null)
    {
        if (processName is null)
        {
            return null;
        }

        if (extraStructuredApps is not null && extraStructuredApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            return StructuredHint;
        }

        if (extraProseApps is not null && extraProseApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            return ProseHint;
        }

        return ProcessNameToHint.TryGetValue(processName, out string? hint) ? hint : null;
    }
}
