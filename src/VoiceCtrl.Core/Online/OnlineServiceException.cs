using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Online;

/// <summary>An Online provider answered with an error, or with no text. Replaces the Gemini-only
/// exception (ticket 60): every provider's failure is judged by the same rules in
/// <see cref="OnlineFailures"/>. The message is the provider's own, never a key.</summary>
public sealed class OnlineServiceException(HttpStatusCode statusCode, string message, OnlineProvider? provider = null)
    : TranscriptionException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Who answered; null where the caller did not say (a test double).</summary>
    public OnlineProvider? Provider { get; } = provider;
}

/// <summary>What an Online failure means, the same for every provider and every job.</summary>
public static class OnlineFailures
{
    /// <summary>
    /// Busy or overloaded on the provider's side, not a bad request: worth trying the next provider
    /// or Offline. 429 and 503 are the measured cases (Gemini's "high demand", NVIDIA's "Worker local
    /// total request limit reached"); 500, 502 and 504 are the same kind of upstream hiccup. 400,
    /// 401, 403 and 404 mean the request or the key is wrong and are never called transient.
    /// </summary>
    public static bool IsTransient(OnlineServiceException ex) => ex.StatusCode is
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.BadGateway or
        HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// The key itself is bad: 401, 403 (a revoked, restricted or leaked key), or a 400 whose message
    /// is about the key. Other 400s are request problems (an unsupported option) and must not be
    /// blamed on the key.
    /// </summary>
    public static bool IsKeyRejection(OnlineServiceException ex) => ex.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => true,
        HttpStatusCode.BadRequest => ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("API_KEY_INVALID", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>The network, not the provider: no connection, or the request's own deadline passed.
    /// False when the caller cancelled, which must stay a cancellation.</summary>
    public static bool IsConnectivity(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    /// <summary>Another provider (or Offline) may do better: busy, unreachable, or this key refused.</summary>
    public static bool IsWorthAnotherTry(Exception ex, CancellationToken cancellationToken) => !cancellationToken.IsCancellationRequested && ex switch
    {
        OnlineServiceException service => IsTransient(service) || IsKeyRejection(service),
        _ => IsConnectivity(ex, cancellationToken),
    };

    /// <summary>A short, key-free description for the log.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        OnlineServiceException service => $"{(int)service.StatusCode} {service.StatusCode}",
        _ => ex.GetType().Name,
    };
}
