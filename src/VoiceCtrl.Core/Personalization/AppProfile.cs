using System.Text.Json.Serialization;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// How dictation into one particular application should read. Every field is optional and a null
/// one means "no opinion, use whatever was going to happen anyway", so a profile can override a
/// single aspect without having to restate the rest.
/// </summary>
public sealed class AppProfile
{
    /// <summary>Free text describing register, e.g. "formal, no contractions". The literal value
    /// "none" suppresses the built-in tone hint for this application instead of adding one.</summary>
    [JsonPropertyName("tone")]
    public string? Tone { get; init; }

    /// <summary>"structured" for Markdown lists, "prose" for continuous sentences, "none" to
    /// suppress the built-in formatting hint.</summary>
    [JsonPropertyName("formatting")]
    public string? Formatting { get; init; }

    /// <summary>"light", "standard" or "aggressive", overriding CLEANUP_LEVEL for this app.</summary>
    [JsonPropertyName("cleanup")]
    public string? Cleanup { get; init; }

    /// <summary>Anything else to tell the model when dictating here.</summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    public const string None = "none";
    public const string Structured = "structured";
    public const string Prose = "prose";
}
