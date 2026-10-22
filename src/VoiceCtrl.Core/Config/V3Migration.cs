using System.IO;
using System.Text.Json;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Reads what a v3 install left behind and turns it into v4 settings (spec §5.2): the key, Mode
/// and tuning from its .env, and Mode, Hotkeys and Theme from its data files. Only ever reads, so
/// every v3 file is left exactly as it was, and a user who goes back to v3 loses nothing.
/// </summary>
public static class V3Migration
{
    private const string PlaceholderApiKey = "your-api-key-here";

    // v3's own files in the data root. Finding any of them means v3 ran on this PC.
    private const string PrefsFile = "prefs.json";
    private const string HotkeyFile = "hotkey.json";
    private const string ThemeFile = "theme.json";
    private static readonly string[] V3DataFiles =
        [PrefsFile, HotkeyFile, ThemeFile, "history.json", "dictionary.txt", "snippets.txt", "profiles.json"];

    // v3 wrote these files with default System.Text.Json options: PascalCase names, enums as names.
    private static readonly JsonSerializerOptions V3JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Everything the migration found. Settings are complete (defaults where v3 had
    /// nothing); the key is null when no usable one was found.</summary>
    public static V3MigrationResult Read(string dataRoot, V3Breadcrumbs breadcrumbs)
    {
        string? envPath = breadcrumbs.EnvCandidateFolders()
            .Select(folder => Path.Combine(folder, ".env"))
            .FirstOrDefault(File.Exists);
        IReadOnlyDictionary<string, string> env =
            (envPath is null ? null : ConfigLoader.ReadEnvFile(envPath)) ?? new Dictionary<string, string>();

        bool foundV3 = envPath is not null || V3DataFiles.Any(name => File.Exists(Path.Combine(dataRoot, name)));

        var defaults = new UserSettings();
        UserSettings settings = defaults with
        {
            Dictation = defaults.Dictation with
            {
                Hotkeys = ReadV3Json<HotkeySettings>(Path.Combine(dataRoot, HotkeyFile)) ?? new HotkeySettings(),
            },
            Transcription = defaults.Transcription with
            {
                Mode = ReadPrefsMode(Path.Combine(dataRoot, PrefsFile)) ?? ModeFromEnv(env),
                DefaultCleanup = NonEmpty(env, "CLEANUP_LEVEL") ?? defaults.Transcription.DefaultCleanup,
            },
            App = defaults.App with
            {
                Theme = ReadThemeFile(Path.Combine(dataRoot, ThemeFile)) ?? defaults.App.Theme,

                // v3 always turned autostart on and had no switch to turn it off, so a missing Run
                // value means the user removed it on purpose.
                StartWithWindows = foundV3 ? breadcrumbs.RunKeyCommand is not null : defaults.App.StartWithWindows,
                SetupComplete = foundV3,
            },
            Advanced = AdvancedFromEnv(env),
        };

        string? key = NonEmpty(env, "GEMINI_API_KEY");
        return new V3MigrationResult(settings, key == PlaceholderApiKey ? null : key, envPath, foundV3);
    }

    // v3's first-run rule, kept: an explicit Offline may be a privacy choice (never send audio to
    // Google), so it stays Offline; anything else becomes Auto, which is Online whenever there's
    // internet and only adds a fallback.
    private static TranscriptionModePreference ModeFromEnv(IReadOnlyDictionary<string, string> env) =>
        string.Equals(NonEmpty(env, "TRANSCRIPTION_MODE"), "Offline", StringComparison.OrdinalIgnoreCase)
            ? TranscriptionModePreference.Offline
            : TranscriptionModePreference.Auto;

    private static TranscriptionModePreference? ReadPrefsMode(string path) =>
        ReadV3Json<V3PrefsFile>(path)?.TranscriptionMode is string mode &&
        Enum.TryParse(mode, ignoreCase: true, out TranscriptionModePreference parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static ThemePreference? ReadThemeFile(string path) =>
        ReadV3Json<V3ThemeFile>(path)?.Theme is string theme &&
        Enum.TryParse(theme, ignoreCase: true, out ThemePreference parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static AdvancedSection AdvancedFromEnv(IReadOnlyDictionary<string, string> env) => new()
    {
        GeminiModelId = NonEmpty(env, "GEMINI_MODEL_ID"),
        DoubleTapWindowMs = Int(env, "DOUBLE_TAP_WINDOW_MS"),
        AutoHideDelayMs = Int(env, "AUTO_HIDE_DELAY_MS"),
        ThinkingLevel = NonEmpty(env, "THINKING_LEVEL"),
        StyleNotes = NonEmpty(env, "DICTATION_STYLE_NOTES"),
        EnableToneAwareness = Bool(env, "ENABLE_TONE_AWARENESS"),
        PromptStyleApps = List(env, "PROMPT_STYLE_APPS"),
        ChatStyleApps = List(env, "CHAT_STYLE_APPS"),
        LocalModelVariant = NonEmpty(env, "LOCAL_MODEL_VARIANT"),
        LocalNumThreads = Int(env, "LOCAL_NUM_THREADS"),
        CompressUpload = Bool(env, "COMPRESS_UPLOAD"),
        SpokenPunctuation = Bool(env, "SPOKEN_PUNCTUATION"),
    };

    // A v3 file that's missing or unreadable just contributes nothing; the migration never fails.
    private static T? ReadV3Json<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), V3JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogInfo($"v3 migration skipped {Path.GetFileName(path)}: {ex.GetType().Name}");
            return null;
        }
    }

    private static string? NonEmpty(IReadOnlyDictionary<string, string> env, string key) =>
        env.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int? Int(IReadOnlyDictionary<string, string> env, string key) =>
        int.TryParse(NonEmpty(env, key), out int parsed) ? parsed : null;

    private static bool? Bool(IReadOnlyDictionary<string, string> env, string key) =>
        bool.TryParse(NonEmpty(env, key), out bool parsed) ? parsed : null;

    private static string[]? List(IReadOnlyDictionary<string, string> env, string key)
    {
        string[] items = NonEmpty(env, key)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return items.Length == 0 ? null : items;
    }

    private sealed class V3PrefsFile
    {
        public string? TranscriptionMode { get; set; }
    }

    private sealed class V3ThemeFile
    {
        public string? Theme { get; set; }
    }
}

/// <summary>What <see cref="V3Migration.Read"/> found. A class, not a record, so the generated
/// ToString can't print the key into a log.</summary>
public sealed class V3MigrationResult(UserSettings settings, string? geminiApiKey, string? envFilePath, bool foundV3)
{
    public UserSettings Settings { get; } = settings;

    /// <summary>Null when no .env was found or its key was blank or the placeholder.</summary>
    public string? GeminiApiKey { get; } = geminiApiKey;

    public string? EnvFilePath { get; } = envFilePath;

    public bool FoundV3 { get; } = foundV3;
}
