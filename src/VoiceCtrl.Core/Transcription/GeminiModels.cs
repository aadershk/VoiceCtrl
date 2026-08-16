using System.Net;
using System.Text.Json.Serialization;

namespace VoiceCtrl.Core.Transcription;

// Request fields are snake_case, confirmed against live REST examples on
// ai.google.dev/gemini-api/docs/generate-content/audio. Response fields are proto3-JSON-canonical
// camelCase; only single-word fields (candidates/content/parts/text) are load-bearing for the
// happy path, so a casing miss on a supplementary field (finishReason, blockReason) degrades to a
// less-specific error message rather than breaking extraction.

internal sealed class GenerateContentRequest
{
    [JsonPropertyName("contents")]
    public required List<RequestContent> Contents { get; init; }

    // Unlike the snake_case request fields above, generationConfig and its nested fields are
    // genuinely camelCase in Google's current REST reference, verified live, not a typo.
    [JsonPropertyName("generationConfig")]
    public GenerationConfig? GenerationConfig { get; init; }
}

internal sealed class GenerationConfig
{
    [JsonPropertyName("thinkingConfig")]
    public ThinkingConfig? ThinkingConfig { get; init; }
}

internal sealed class ThinkingConfig
{
    [JsonPropertyName("thinkingLevel")]
    public required string ThinkingLevel { get; init; }
}

internal sealed class RequestContent
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("parts")]
    public required List<RequestPart> Parts { get; init; }
}

internal sealed class RequestPart
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("inline_data")]
    public InlineData? InlineData { get; init; }

    [JsonPropertyName("file_data")]
    public FileData? FileData { get; init; }
}

internal sealed class InlineData
{
    [JsonPropertyName("mime_type")]
    public required string MimeType { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

internal sealed class FileData
{
    [JsonPropertyName("mime_type")]
    public required string MimeType { get; init; }

    [JsonPropertyName("file_uri")]
    public required string FileUri { get; init; }
}

internal sealed class FileUploadResponse
{
    [JsonPropertyName("file")]
    public UploadedFile? File { get; init; }
}

internal sealed class UploadedFile
{
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }
}

internal sealed class GenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public List<ResponseCandidate>? Candidates { get; init; }

    [JsonPropertyName("promptFeedback")]
    public PromptFeedback? PromptFeedback { get; init; }
}

internal sealed class ResponseCandidate
{
    [JsonPropertyName("content")]
    public ResponseContent? Content { get; init; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; init; }
}

internal sealed class ResponseContent
{
    [JsonPropertyName("parts")]
    public List<ResponsePart>? Parts { get; init; }
}

internal sealed class ResponsePart
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class PromptFeedback
{
    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; init; }
}

internal sealed class GeminiErrorEnvelope
{
    [JsonPropertyName("error")]
    public GeminiErrorDetail? Error { get; init; }
}

internal sealed class GeminiErrorDetail
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed class GeminiApiException(HttpStatusCode statusCode, string message) : TranscriptionException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
