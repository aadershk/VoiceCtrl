namespace VoiceCtrl.Core.Config;

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
    /// built-in table, from .env's PROMPT_STYLE_APPS. Lets a wrong built-in guess (e.g. the Claude
    /// desktop app's exact process name) be corrected without a code change.</summary>
    public required IReadOnlyList<string> PromptStyleApps { get; init; }

    /// <summary>Same as <see cref="PromptStyleApps"/> but for the prose/chat-style bucket, from
    /// .env's CHAT_STYLE_APPS.</summary>
    public required IReadOnlyList<string> ChatStyleApps { get; init; }

    /// <summary>"Online" (Gemini, cloud) or "Offline" (local model, no network call). Normalized
    /// by <see cref="ConfigLoader"/> to exactly one of these two values regardless of .env casing.
    /// This is only the .env-level seed value. The live, tray-switchable Auto/Online/Offline
    /// preference lives in TranscriptionModeStore, not here.</summary>
    public required string TranscriptionMode { get; init; }

    /// <summary>The raw explicit TRANSCRIPTION_MODE from .env ("Online"/"Offline"), or null if it
    /// was unset/unrecognized and <see cref="TranscriptionMode"/> above is therefore just the silent
    /// default rather than a real user choice. Consulted exactly once, by TranscriptionModeStore's
    /// first-run migration seed, so an explicit Offline choice (e.g. for privacy, meaning never send audio
    /// to Google) isn't silently switched to Auto.</summary>
    public string? ExplicitTranscriptionMode { get; init; }

    public required string LocalModelVariant { get; init; }

    /// <summary>
    /// False when the key is blank or still the placeholder from .env.example, so the app should
    /// still start in this state (tray + hotkey usable), only refusing at the point a
    /// transcription is actually attempted, so first-run users always have a way to reach
    /// Settings and fix it.
    /// </summary>
    public bool IsApiKeyConfigured =>
        !string.IsNullOrWhiteSpace(GeminiApiKey) && GeminiApiKey != PlaceholderApiKey;
}
