namespace VoiceCtrl.Core.Config;

/// <summary>A read-only snapshot of the settings the transcription and Overlay code need, built
/// once at startup by <see cref="ConfigLoader.Load(UserSettings, string)"/>.</summary>
public sealed class AppConfig
{
    private const string PlaceholderApiKey = "your-api-key-here";

    public required string GeminiApiKey { get; init; }
    public required string GeminiModelId { get; init; }
    public required int DoubleTapWindowMs { get; init; }
    public required int AutoHideDelayMs { get; init; }
    public required string ThinkingLevel { get; init; }
    public required string StyleNotes { get; init; }
    public required bool EnableToneAwareness { get; init; }
    public required string CleanupLevel { get; init; }

    /// <summary>Process names (e.g. "windowsterminal") that should always get structured/Markdown
    /// formatting hints, on top of <see cref="VoiceCtrl.Core.Transcription.FormattingHintMapper"/>'s
    /// built-in table, from the advanced settings (v3's PROMPT_STYLE_APPS). Lets a wrong built-in guess (e.g. the Claude
    /// desktop app's exact process name) be corrected without a code change.</summary>
    public required IReadOnlyList<string> PromptStyleApps { get; init; }

    /// <summary>Same as <see cref="PromptStyleApps"/> but for the prose/chat-style bucket (v3's
    /// CHAT_STYLE_APPS).</summary>
    public required IReadOnlyList<string> ChatStyleApps { get; init; }

    public required string LocalModelVariant { get; init; }

    /// <summary>
    /// ONNX intra-op threads for the local model (v3's LOCAL_NUM_THREADS). Defaults to half
    /// the machine's logical cores, bounded, rather than a fixed number: the transcription is a
    /// burst of work with nothing else competing for the CPU, and the old hardcoded 2 left most of
    /// a modern machine idle. Half rather than all, because the app runs in the background and must
    /// not stall whatever the user is actually working in.
    /// </summary>
    public required int LocalNumThreads { get; init; }

    /// <summary>
    /// Whether to MP3-compress audio before uploading it (v3's COMPRESS_UPLOAD). Off until
    /// the transcript comparison behind it has been run against a live API key: the saving is real
    /// but it is not worth risking accuracy on an unverified assumption.
    /// </summary>
    public required bool CompressUpload { get; init; }

    /// <summary>
    /// Whether Offline mode turns spoken "comma", "period" and friends into the characters (v3's
    /// SPOKEN_PUNCTUATION). Off by default because those are ordinary English words and
    /// there is no reliable way from text alone to tell "add a comma here" from "the comma is
    /// missing". Online mode needs no such switch: it still has the audio, so it can hear the
    /// difference. Line breaks ("new paragraph") are always on and are guarded separately.
    /// </summary>
    public required bool SpokenPunctuation { get; init; }

    /// <summary>
    /// False when the key is blank or still v3's .env.example placeholder, so the app should
    /// still start in this state (tray + hotkey usable), only refusing at the point a
    /// transcription is actually attempted, so first-run users always have a way to reach
    /// Settings and fix it.
    /// </summary>
    public bool IsApiKeyConfigured => IsUsableApiKey(GeminiApiKey);

    /// <summary>The <see cref="IsApiKeyConfigured"/> rule for a key read live from the secrets store.</summary>
    public static bool IsUsableApiKey(string key) =>
        !string.IsNullOrWhiteSpace(key) && key != PlaceholderApiKey;
}
