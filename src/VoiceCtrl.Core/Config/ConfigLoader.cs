using System.IO;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Hand-rolled .env parser, deliberately not a NuGet dependency (DotNetEnv etc.), matching the
/// project's general preference for owning small, fully-understood pieces of wire/config format
/// over pulling in a package for a ~20-line job.
/// </summary>
public static class ConfigLoader
{
    internal const string DefaultModelId = "gemini-3.7-flash";
    private const int DefaultDoubleTapWindowMs = 400;
    private const int DefaultAutoHideDelayMs = 1500;

    // "low", not "minimal" - gemini-3.7-flash (DefaultModelId above) rejects thinkingLevel=minimal
    // with a 4xx ("not supported for this model"), so "minimal" would break Online transcription
    // out of the box for every default install. "low" is the fastest level this model accepts.
    private const string DefaultThinkingLevel = "low";
    private const string DefaultCleanupLevel = "standard";
    private const string DefaultTranscriptionMode = "Online";
    internal const string DefaultLocalModelVariant = "parakeet-tdt-0.6b-v2";

    // Off by default: MP3 upload cuts the payload by roughly a factor of ten, but nothing about
    // accuracy at 32kbps has been verified against the live API yet, and a default that might
    // quietly degrade transcripts is worse than one that is merely slower.
    private const bool DefaultCompressUpload = false;

    // Off by default: "comma" and "period" are ordinary words, and a speaker who says "the comma
    // is missing" would get "the , is missing". Opt-in for people who dictate punctuation aloud
    // deliberately, which is a habit rather than the common case.
    private const bool DefaultSpokenPunctuation = false;

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
            LocalNumThreads = ResolveLocalNumThreads(values.GetValueOrDefault("LOCAL_NUM_THREADS")),
            CompressUpload = ParseBoolOrDefault(values.GetValueOrDefault("COMPRESS_UPLOAD"), DefaultCompressUpload),
            SpokenPunctuation = ParseBoolOrDefault(values.GetValueOrDefault("SPOKEN_PUNCTUATION"), DefaultSpokenPunctuation),
        };
    }

    /// <summary>
    /// Scales the local model's thread count to the machine. The upper bound is there because
    /// ONNX intra-op parallelism stops paying for itself well before it runs out of cores on a
    /// model this size, and past that point the extra threads only add contention.
    /// A configured value is clamped rather than rejected: 0 or a negative would make sherpa-onnx
    /// behave unpredictably, and a wildly high one would thrash.
    /// </summary>
    private static int ResolveLocalNumThreads(string? configured)
    {
        const int minThreads = 2;
        const int maxThreads = 8;

        int resolved = int.TryParse(configured, out int parsed)
            ? parsed
            : Environment.ProcessorCount / 2;

        return Math.Clamp(resolved, minThreads, maxThreads);
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
