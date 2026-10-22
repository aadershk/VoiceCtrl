using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VoiceCtrl.Core.Personalization;

public enum TypeSafeOutcome
{
    /// <summary>TypeSafe answered every question.</summary>
    Answered,

    /// <summary>TypeSafe refused the key (401 or 403).</summary>
    KeyRefused,

    /// <summary>No internet, a timeout, TypeSafe busy after the retries, or an answer that
    /// couldn't be read. Nothing is known about the key.</summary>
    Unavailable,
}

/// <param name="Nouls">Keyed by <see cref="SuggestionCandidate.Word"/>: the probability that it is
/// worth a Dictionary entry. Empty unless <see cref="Outcome"/> is Answered.</param>
public sealed record TypeSafeJudgement(TypeSafeOutcome Outcome, IReadOnlyDictionary<string, double> Nouls)
{
    public static TypeSafeJudgement Failed(TypeSafeOutcome outcome) => new(outcome, new Dictionary<string, double>());
}

/// <summary>What <see cref="DictionarySuggestions"/> asks TypeSafe; a seam so tests can count calls.</summary>
public interface ISuggestionJudge
{
    /// <summary>A minimal request that proves the key works. Sends no Dictation text.</summary>
    Task<TypeSafeOutcome> CheckKeyAsync(string key, CancellationToken cancellationToken);

    /// <summary>One request with one yes/no question per candidate.</summary>
    Task<TypeSafeJudgement> JudgeAsync(string key, IReadOnlyList<SuggestionCandidate> candidates, CancellationToken cancellationToken);
}

/// <summary>
/// TypeSafe's Jev over its HTTP API (research 06; docs.typesafe.ai/api): one POST to
/// <c>/v1/systemone</c> with the user's own key, a text state and typed questions. There is no .NET
/// SDK, so the SDKs' retry on 429 and 529 is done here. The model is pinned rather than
/// <c>jev-latest</c>, because the 0.7 threshold in <see cref="DictionarySuggestions"/> was chosen for
/// this version and an alias can move under it.
/// </summary>
public sealed class TypeSafeClient(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null) : ISuggestionJudge
{
    public static readonly Uri Endpoint = new("https://api.typesafe.ai/v1/systemone");

    public const string Model = "jev-1.13.0";

    private static readonly TimeSpan[] RetryWaits = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    // The judgment the user's words get. Names and jargon are what a recognizer misspells; well-known
    // words and brands it already gets right, so adding them only clutters the Dictionary.
    private const string Instructions =
        "Would a speech recognizer need to be told how to spell `candidates[{0}].word`? " +
        "`candidates[{0}].example` is a sentence the speaker dictated with it.";

    private static readonly JsonObject Criteria = new()
    {
        ["true"] = "It is a personal name, a company, product or project name, an acronym, or a specialised " +
            "technical term from the speaker's own work: worth adding to their personal dictionary.",
        ["false"] = "It is an ordinary word, a well-known brand, place or service that any recognizer already " +
            "spells, a day, month, country or language, or not a real term at all.",
    };

    public async Task<TypeSafeOutcome> CheckKeyAsync(string key, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["state"] = "VoiceCtrl",
            ["model"] = Model,
            ["questions"] = new JsonObject { ["k"] = new JsonObject { ["type"] = "noul", ["instructions"] = "Is `state` a word?" } },
        };

        (TypeSafeOutcome outcome, _) = await PostAsync(key, body, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    public async Task<TypeSafeJudgement> JudgeAsync(string key, IReadOnlyList<SuggestionCandidate> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return new TypeSafeJudgement(TypeSafeOutcome.Answered, new Dictionary<string, double>());
        }

        var state = new JsonArray();
        var questions = new JsonObject();
        for (int i = 0; i < candidates.Count; i++)
        {
            state.Add(new JsonObject { ["word"] = candidates[i].Word, ["example"] = candidates[i].Example });
            questions[$"c{i}"] = new JsonObject
            {
                ["type"] = "noul",
                ["instructions"] = string.Format(System.Globalization.CultureInfo.InvariantCulture, Instructions, i),
                ["criteria"] = Criteria.DeepClone(),
            };
        }

        var body = new JsonObject { ["state"] = new JsonObject { ["candidates"] = state }, ["model"] = Model, ["questions"] = questions };
        (TypeSafeOutcome outcome, JsonNode? answers) = await PostAsync(key, body, cancellationToken).ConfigureAwait(false);
        if (outcome != TypeSafeOutcome.Answered)
        {
            return TypeSafeJudgement.Failed(outcome);
        }

        var nouls = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (answers?[$"c{i}"]?["noul"] is not JsonValue value || !value.TryGetValue(out double noul))
            {
                return TypeSafeJudgement.Failed(TypeSafeOutcome.Unavailable);
            }

            nouls[candidates[i].Word] = noul;
        }

        return new TypeSafeJudgement(TypeSafeOutcome.Answered, nouls);
    }

    /// <summary>Sends <paramref name="body"/>, retrying a busy or rate-limited answer twice.
    /// Returns the response's <c>answers</c> when Answered.</summary>
    private async Task<(TypeSafeOutcome, JsonNode?)> PostAsync(string key, JsonObject body, CancellationToken cancellationToken)
    {
        string json = body.ToJsonString();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(AttemptTimeout);
                using HttpResponseMessage response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        string text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                        return (TypeSafeOutcome.Answered, JsonNode.Parse(text)?["answers"]);
                    case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                        return (TypeSafeOutcome.KeyRefused, null);
                    case HttpStatusCode.TooManyRequests or (HttpStatusCode)529 when attempt < RetryWaits.Length:
                        await _delay(RetryWaits[attempt], cancellationToken).ConfigureAwait(false);
                        continue;
                    default:
                        return (TypeSafeOutcome.Unavailable, null);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return (TypeSafeOutcome.Unavailable, null);
            }
        }
    }
}
