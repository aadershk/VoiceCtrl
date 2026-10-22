using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Online;

/// <summary>One message of a chat completions request: text, optionally with a WAV clip.</summary>
public sealed record ChatMessage(string Role, string Text, byte[]? Audio = null)
{
    public static ChatMessage System(string text) => new("system", text);

    public static ChatMessage User(string text, byte[]? audio = null) => new("user", text, audio);

    public static ChatMessage Assistant(string text) => new("assistant", text);
}

/// <summary>
/// The OpenAI chat completions wire format (<c>POST {base}/chat/completions</c>, Bearer key), which
/// NVIDIA, OpenAI, Groq, OpenRouter, Mistral and many others speak, so NVIDIA, OpenAI and Other are
/// presets of this one client (ticket 60). The only file that knows the request and response shapes.
/// </summary>
public static partial class ChatCompletions
{
    /// <summary>The request body for <paramref name="messages"/>. A clip goes in the shape
    /// <paramref name="endpoint"/>'s model hears; a reasoning model is asked not to think first.</summary>
    public static JsonObject Body(OnlineEndpoint endpoint, string model, IReadOnlyList<ChatMessage> messages, int? maxTokens = null)
    {
        var list = new JsonArray();
        foreach (ChatMessage message in messages)
        {
            list.Add(Message(message, endpoint.Audio));
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = list,
        };

        if (endpoint.SendsTemperature)
        {
            body["temperature"] = 0;
        }

        if (endpoint.TextReasoningEffort is { } effort && endpoint.IsTextModel(model))
        {
            body["reasoning_effort"] = effort;
        }

        if (maxTokens is { } max)
        {
            body["max_tokens"] = max;
        }

        // The chat template's own switch. A "/no_think" system line works too, but made super-vl
        // write 255 tokens of reasoning into its answer (2026-10-21).
        if (endpoint.NoThink)
        {
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        }

        return body;
    }

    private static JsonObject Message(ChatMessage message, AudioInput audio)
    {
        if (message.Audio is null)
        {
            return new JsonObject { ["role"] = message.Role, ["content"] = message.Text };
        }

        var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = message.Text } };
        parts.Add(AudioPart(message.Audio, audio));
        return new JsonObject { ["role"] = message.Role, ["content"] = parts };
    }

    /// <summary>NVIDIA's Nemotron omni hears only an <c>audio_url</c> data URI and silently ignores
    /// <c>input_audio</c> (measured 2026-10-21); OpenAI's audio models take <c>input_audio</c>.</summary>
    internal static JsonObject AudioPart(byte[] wav, AudioInput audio)
    {
        string base64 = Convert.ToBase64String(wav);
        return audio == AudioInput.AudioUrl
            ? new JsonObject
            {
                ["type"] = "audio_url",
                ["audio_url"] = new JsonObject { ["url"] = "data:audio/wav;base64," + base64 },
            }
            : new JsonObject
            {
                ["type"] = "input_audio",
                ["input_audio"] = new JsonObject { ["data"] = base64, ["format"] = "wav" },
            };
    }

    /// <summary>Sends one request and returns the reply's text, with any thinking block taken off.
    /// Throws <see cref="OnlineServiceException"/> for an error status or an empty reply, and what
    /// <see cref="HttpClient"/> throws for the network; <paramref name="timeout"/> ends it as a
    /// <see cref="TaskCanceledException"/> that is not the caller's cancellation.</summary>
    public static async Task<string> CompleteAsync(
        HttpClient http,
        OnlineEndpoint endpoint,
        string key,
        string model,
        IReadOnlyList<ChatMessage> messages,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int? maxTokens = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.BaseUrl.TrimEnd('/') + "/chat/completions");
        request.Headers.Add("Authorization", "Bearer " + key);
        request.Content = new StringContent(Body(endpoint, model, messages, maxTokens).ToJsonString(), Encoding.UTF8, "application/json");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using HttpResponseMessage response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OnlineServiceException(response.StatusCode, ErrorMessage(body), endpoint.Provider);
        }

        string? text = ReplyText(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new OnlineServiceException(response.StatusCode, $"{endpoint.Name} returned no text.", endpoint.Provider);
        }

        return text.Trim();
    }

    /// <summary>The first choice's text, or null. A reply that thought out loud has the thinking taken off.</summary>
    internal static string? ReplyText(string body)
    {
        try
        {
            JsonNode? content = JsonNode.Parse(body)?["choices"]?[0]?["message"]?["content"];
            string? text = content switch
            {
                JsonValue value when value.TryGetValue(out string? s) => s,
                // Some servers answer with content parts, like a request's.
                JsonArray parts => string.Concat(parts.Select(part => part?["text"]?.GetValue<string>() ?? string.Empty)),
                _ => null,
            };
            return text is null ? null : ThinkBlock().Replace(text, string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The provider's own words for an error: OpenAI's <c>error.message</c>, NVIDIA's
    /// <c>detail</c>, or the body itself.</summary>
    internal static string ErrorMessage(string body)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(body);
            string? message = root?["error"] switch
            {
                JsonObject error => error["message"]?.GetValue<string>(),
                JsonValue value when value.TryGetValue(out string? s) => s,
                _ => null,
            };
            message ??= root?["detail"]?.GetValue<string>() ?? root?["title"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(message) ? Shorten(body) : message;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Shorten(body);
        }
    }

    // An error page can be long HTML; the log needs a line.
    private static string Shorten(string body) => body.Length <= 300 ? body : body[..300];

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();
}
