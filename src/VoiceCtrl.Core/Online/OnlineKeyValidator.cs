using System.IO;
using System.Net;
using System.Net.Http;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Online;

/// <summary>What a key check found.</summary>
public enum KeyValidation
{
    Valid,
    InvalidKey,
    NetworkError,
    Timeout,
    Unknown,
}

/// <summary>A key check's answer, and for Other what kind of model it reached.</summary>
/// <param name="Audio">How the model hears a clip; <see cref="AudioInput.Unknown"/> when the check
/// did not look (a preset) or could not tell (busy).</param>
public sealed record KeyCheckOutcome(KeyValidation Result, AudioInput Audio = AudioInput.Unknown);

/// <summary>
/// Checks a provider's key against the real service before it is saved (ticket 60). NVIDIA lists its
/// models without a key, so the check is a one-word chat request to the model Online will use. For
/// Other it also finds out whether the model hears audio, and in which shape, by sending a tiny clip
/// of three spoken digits: a model that hears it writes them back.
/// </summary>
public static class OnlineKeyValidator
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    /// <summary>"Seven, three, nine", 2 s, synthesised with Windows' own voice.</summary>
    private static readonly Lazy<byte[]> ProbeClip = new(() =>
    {
        using Stream stream = typeof(OnlineKeyValidator).Assembly.GetManifestResourceStream("VoiceCtrl.Online.probe-digits.wav")
            ?? throw new InvalidOperationException("The probe clip is missing from the build.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    });

    public static async Task<KeyCheckOutcome> CheckAsync(
        OnlineEndpoint endpoint, string key, HttpClient? httpClient = null, CancellationToken cancellationToken = default)
    {
        if (endpoint.Provider == OnlineProvider.Gemini)
        {
            return new(await GeminiApiKeyValidator.ValidateAsync(key, httpClient, endpoint.AudioModel, cancellationToken).ConfigureAwait(false));
        }

        HttpClient client = httpClient ?? new HttpClient();
        try
        {
            KeyValidation result = await AskAsync(client, endpoint, key, [ChatMessage.User("Reply with the word OK.")], endpoint.AudioModel, cancellationToken)
                .ConfigureAwait(false) switch
            {
                null => KeyValidation.Valid,
                { } failure => failure,
            };

            if (result != KeyValidation.Valid || endpoint.Provider != OnlineProvider.Other)
            {
                return new(result);
            }

            return new(result, await DetectAudioAsync(client, endpoint, key, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            if (httpClient is null)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Sends the probe clip as <c>audio_url</c>, then as <c>input_audio</c>; the first shape the model
    /// writes the digits back for is the one it hears. A model that refuses both, or answers without
    /// the digits, is text-only. A busy or failed probe leaves it <see cref="AudioInput.Unknown"/>.
    /// </summary>
    internal static async Task<AudioInput> DetectAudioAsync(HttpClient client, OnlineEndpoint endpoint, string key, CancellationToken cancellationToken)
    {
        bool undecided = false;
        foreach (AudioInput shape in new[] { AudioInput.AudioUrl, AudioInput.InputAudio })
        {
            try
            {
                string reply = await ChatCompletions.CompleteAsync(
                    client, endpoint with { Audio = shape }, key, endpoint.AudioModel,
                    [ChatMessage.User("Write down the words spoken in the attached audio. Reply with the words only.", ProbeClip.Value)],
                    CheckTimeout, cancellationToken, maxTokens: 40).ConfigureAwait(false);
                if (HeardTheDigits(reply))
                {
                    return shape;
                }
            }
            catch (OnlineServiceException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotImplemented)
            {
                // This shape is refused: a text-only model, or the other shape.
            }
            catch (Exception ex) when (OnlineFailures.IsWorthAnotherTry(ex, cancellationToken) || ex is OnlineServiceException)
            {
                undecided = true;
            }
        }

        return undecided ? AudioInput.Unknown : AudioInput.None;
    }

    internal static bool HeardTheDigits(string reply)
    {
        string lower = reply.ToLowerInvariant();
        int heard = new[] { ("seven", '7'), ("three", '3'), ("nine", '9') }
            .Count(digit => lower.Contains(digit.Item1, StringComparison.Ordinal) || lower.Contains(digit.Item2));
        return heard >= 2;
    }

    /// <summary>Null when the key worked; otherwise what went wrong.</summary>
    private static async Task<KeyValidation?> AskAsync(
        HttpClient client, OnlineEndpoint endpoint, string key, IReadOnlyList<ChatMessage> messages, string model, CancellationToken cancellationToken)
    {
        try
        {
            await ChatCompletions.CompleteAsync(client, endpoint, key, model, messages, CheckTimeout, cancellationToken, maxTokens: 16).ConfigureAwait(false);
            return null;
        }
        catch (OnlineServiceException ex)
        {
            // 404 is a wrong address or model, which only Other can have: the entry didn't work.
            return OnlineFailures.IsKeyRejection(ex) || ex.StatusCode == HttpStatusCode.NotFound
                ? KeyValidation.InvalidKey
                : ex.StatusCode == HttpStatusCode.OK ? null : KeyValidation.Unknown;
        }
        catch (HttpRequestException)
        {
            return KeyValidation.NetworkError;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return KeyValidation.Timeout;
        }
        catch (UriFormatException)
        {
            return KeyValidation.InvalidKey;
        }
        catch (InvalidOperationException)
        {
            // HttpClient refuses an address that is not absolute http(s).
            return KeyValidation.InvalidKey;
        }
    }
}
