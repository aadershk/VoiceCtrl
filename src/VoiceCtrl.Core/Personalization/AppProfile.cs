using System.Text.Json.Serialization;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// How dictation into one particular application should read. Every field is optional and a null
/// one means "no opinion, use whatever was going to happen anyway", so a profile can override a
/// single aspect without having to restate the rest.
/// </summary>
public sealed class AppProfile
{
    /// <summary>The app's display name ("Outlook"), kept so the Hub can name a Profile whose app
    /// isn't running. Null in a hand-written file: the Hub then shows the process name.</summary>
    [JsonPropertyName("app")]
    public string? App { get; init; }

    /// <summary>Free text describing register, e.g. "formal, no contractions". The literal value
    /// "none" suppresses the built-in tone hint for this application instead of adding one.</summary>
    [JsonPropertyName("tone")]
    public string? Tone { get; init; }

    /// <summary>The app's own Style ("formal", "casual", "veryCasual" or "excited"), which wins over
    /// its kind's on the Style screen. Null follows the kind.</summary>
    [JsonPropertyName("style")]
    public string? Style { get; init; }

    /// <summary>"structured" for Markdown lists, "prose" for short chat lines, "paragraphs" for a
    /// document's paragraphs and headings, "none" to suppress the built-in formatting hint.</summary>
    [JsonPropertyName("formatting")]
    public string? Formatting { get; init; }

    /// <summary>A Cleanup Level ("verbatim", "light", "standard" or "aggressive") for this app, or
    /// null to use the Default cleanup from Settings.</summary>
    [JsonPropertyName("cleanup")]
    public string? Cleanup { get; init; }

    /// <summary>True lays the Transcription out as an email, false never does. Null follows the
    /// app's kind: on for an email app, off for every other.</summary>
    [JsonPropertyName("emailLayout")]
    public bool? EmailLayout { get; init; }

    /// <summary>"How I write here": anything else to tell the model when dictating here.</summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    public const string None = "none";
    public const string Structured = "structured";
    public const string Prose = "prose";
    public const string Paragraphs = "paragraphs";
}
