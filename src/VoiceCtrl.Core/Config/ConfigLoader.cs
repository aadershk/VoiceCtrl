using System.IO;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Hand-rolled .env parser, deliberately not a NuGet dependency (DotNetEnv etc.), matching the
/// project's general preference for owning small, fully-understood pieces of wire/config format
/// over pulling in a package for a ~20-line job.
/// </summary>
public static class ConfigLoader
{
    private const string DefaultModelId = "gemini-3.7-flash";
    private const int DefaultDoubleTapWindowMs = 400;
    private const int DefaultAutoHideDelayMs = 1500;

    // "low", not "minimal" - gemini-3.7-flash (DefaultModelId above) rejects thinkingLevel=minimal
    // with a 4xx ("not supported for this model"), so "minimal" would break Online transcription
    // out of the box for every default install. "low" is the fastest level this model accepts.
    private const string DefaultThinkingLevel = "low";
    private const string DefaultCleanupLevel = "standard";
    private const string DefaultTranscriptionMode = "Online";
    private const string DefaultLocalModelVariant = "parakeet-tdt-0.6b-v2";

    public static AppConfig Load(string envFilePath)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(envFilePath))
        {
            foreach (string rawLine in File.ReadAllLines(envFilePath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line[..separatorIndex].Trim();
                string value = line[(separatorIndex + 1)..].Trim();
                values[key] = value;
            }
        }

        string? explicitMode = NormalizeMode(values.GetValueOrDefault("TRANSCRIPTION_MODE"));

        return new AppConfig
        {
            GeminiApiKey = values.GetValueOrDefault("GEMINI_API_KEY", string.Empty),
            GeminiModelId = NonEmptyOrDefault(values.GetValueOrDefault("GEMINI_MODEL_ID"), DefaultModelId),
            DoubleTapWindowMs = ParseIntOrDefault(values.GetValueOrDefault("DOUBLE_TAP_WINDOW_MS"), DefaultDoubleTapWindowMs),
            AutoHideDelayMs = ParseIntOrDefault(values.GetValueOrDefault("AUTO_HIDE_DELAY_MS"), DefaultAutoHideDelayMs),
            ThinkingLevel = NonEmptyOrDefault(values.GetValueOrDefault("THINKING_LEVEL"), DefaultThinkingLevel),
            StyleNotes = values.GetValueOrDefault("DICTATION_STYLE_NOTES", string.Empty),
            EnableToneAwareness = ParseBoolOrDefault(values.GetValueOrDefault("ENABLE_TONE_AWARENESS"), true),
            CleanupLevel = NonEmptyOrDefault(values.GetValueOrDefault("CLEANUP_LEVEL"), DefaultCleanupLevel),
            PromptStyleApps = ParseList(values.GetValueOrDefault("PROMPT_STYLE_APPS")),
            ChatStyleApps = ParseList(values.GetValueOrDefault("CHAT_STYLE_APPS")),
            TranscriptionMode = explicitMode ?? DefaultTranscriptionMode,
            ExplicitTranscriptionMode = explicitMode,
            LocalModelVariant = NonEmptyOrDefault(values.GetValueOrDefault("LOCAL_MODEL_VARIANT"), DefaultLocalModelVariant),
        };
    }

    private static string NonEmptyOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static IReadOnlyList<string> ParseList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Normalizes to exactly "Online" or "Offline" regardless of .env casing, or null for anything
    // unrecognized (blank, typo) rather than throwing, matching this loader's existing never-crash-
    // on-bad-config behavior for every other setting. Null vs. a real value also distinguishes "the
    // user never set this" from "the user explicitly chose Online", which TranscriptionModeStore's
    // first-run migration seed depends on.
    private static string? NormalizeMode(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "OFFLINE" => "Offline",
            "ONLINE" => "Online",
            _ => null,
        };

    private static int ParseIntOrDefault(string? value, int fallback) =>
        int.TryParse(value, out int parsed) ? parsed : fallback;

    private static bool ParseBoolOrDefault(string? value, bool fallback) =>
        bool.TryParse(value, out bool parsed) ? parsed : fallback;
}
