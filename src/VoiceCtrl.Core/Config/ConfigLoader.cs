using System.IO;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Builds the <see cref="AppConfig"/> the transcription and Overlay code reads, from the settings
/// store and the Gemini key, and applies the built-in default wherever
/// <see cref="AdvancedSection"/> has no override. Also keeps v3's hand-rolled .env parser, which v4
/// only uses to read a v3 install's .env during the one-time <see cref="V3Migration"/>.
/// </summary>
public static class ConfigLoader
{
    internal const string DefaultModelId = Online.OnlinePresets.GeminiModel;
    private const int DefaultDoubleTapWindowMs = 400;
    private const int DefaultAutoHideDelayMs = 1500;

    // "low", not "minimal" - gemini-3.7-flash (DefaultModelId above) rejects thinkingLevel=minimal
    // with a 4xx ("not supported for this model"), so "minimal" would break Online transcription
    // out of the box for every default install. "low" is the fastest level this model accepts.
    private const string DefaultThinkingLevel = "low";
    private const string DefaultCleanupLevel = "standard";
    internal const string DefaultLocalModelVariant = "parakeet-tdt-0.6b-v2";

    // Off by default: MP3 upload cuts the payload by roughly a factor of ten, but nothing about
    // accuracy at 32kbps has been verified against the live API yet, and a default that might
    // quietly degrade transcripts is worse than one that is merely slower.
    private const bool DefaultCompressUpload = false;

    // Off by default: "comma" and "period" are ordinary words, and a speaker who says "the comma
    // is missing" would get "the , is missing". Opt-in for people who dictate punctuation aloud
    // deliberately, which is a habit rather than the common case.
    private const bool DefaultSpokenPunctuation = false;

    /// <summary>The config for this run. The key comes from <see cref="SecretsStore"/>, never from
    /// the settings file.</summary>
    public static AppConfig Load(UserSettings settings, string geminiApiKey)
    {
        AdvancedSection advanced = settings.Advanced;

        return new AppConfig
        {
            GeminiApiKey = geminiApiKey,
            GeminiModelId = NonEmptyOrDefault(advanced.GeminiModelId, DefaultModelId),
            DoubleTapWindowMs = advanced.DoubleTapWindowMs ?? DefaultDoubleTapWindowMs,
            AutoHideDelayMs = advanced.AutoHideDelayMs ?? DefaultAutoHideDelayMs,
            ThinkingLevel = NonEmptyOrDefault(advanced.ThinkingLevel, DefaultThinkingLevel),
            StyleNotes = advanced.StyleNotes ?? string.Empty,
            EnableToneAwareness = advanced.EnableToneAwareness ?? true,
            CleanupLevel = NonEmptyOrDefault(settings.Transcription.DefaultCleanup, DefaultCleanupLevel),
            PromptStyleApps = advanced.PromptStyleApps ?? [],
            ChatStyleApps = advanced.ChatStyleApps ?? [],
            LocalModelVariant = NonEmptyOrDefault(advanced.LocalModelVariant, DefaultLocalModelVariant),
            LocalNumThreads = ResolveLocalNumThreads(advanced.LocalNumThreads),
            CompressUpload = advanced.CompressUpload ?? DefaultCompressUpload,
            SpokenPunctuation = advanced.SpokenPunctuation ?? DefaultSpokenPunctuation,
        };
    }

    /// <summary>
    /// A v3 .env's KEY=value lines, keys matched case-insensitively and both sides trimmed. Blank
    /// lines, # comments and lines without "=" are skipped. Null when the file doesn't exist or
    /// can't be read. Hand-rolled rather than a NuGet dependency: it's a ~20-line format.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ReadEnvFile(string envFilePath)
    {
        string[] lines;
        try
        {
            if (!File.Exists(envFilePath))
            {
                return null;
            }

            lines = File.ReadAllLines(envFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in lines)
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

            values[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
        }

        return values;
    }

    /// <summary>
    /// Scales the local model's thread count to the machine. The upper bound is there because
    /// ONNX intra-op parallelism stops paying for itself well before it runs out of cores on a
    /// model this size, and past that point the extra threads only add contention.
    /// A configured value is clamped rather than rejected: 0 or a negative would make sherpa-onnx
    /// behave unpredictably, and a wildly high one would thrash.
    /// </summary>
    private static int ResolveLocalNumThreads(int? configured)
    {
        const int minThreads = 2;
        const int maxThreads = 8;

        return Math.Clamp(configured ?? Environment.ProcessorCount / 2, minThreads, maxThreads);
    }

    private static string NonEmptyOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
