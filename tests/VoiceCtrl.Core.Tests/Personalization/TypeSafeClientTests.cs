using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Tests.Personalization;

public sealed class TypeSafeClientTests
{
    private const string Key = "ts_live_1234567890";

    private readonly List<(HttpRequestMessage Request, string Body)> _sent = [];
    private readonly Queue<HttpResponseMessage> _answers = new();
    private readonly List<TimeSpan> _waits = [];
    private readonly TypeSafeClient _client;

    public TypeSafeClientTests()
    {
        _client = new TypeSafeClient(new HttpClient(new FakeHandler(this)), (wait, _) =>
        {
            _waits.Add(wait);
            return Task.CompletedTask;
        });
    }

    private static readonly SuggestionCandidate[] Candidates =
    [
        new("Maya Ortiz", 6, "Send it to Maya Ortiz."),
        new("Zoom", 4, "Join the Zoom call."),
    ];

    [Fact]
    public async Task Judging_PostsOneRequest_WithTheKeyThePinnedModelAndOneNoulPerCandidate()
    {
        Answer(HttpStatusCode.OK, """{"model":"jev-1.13.0","answers":{"c0":{"type":"noul","noul":0.93},"c1":{"type":"noul","noul":0.12}},"usage":{"input_tokens":300,"output_tokens":2}}""");

        await _client.JudgeAsync(Key, Candidates, CancellationToken.None);

        (HttpRequestMessage request, string body) = Assert.Single(_sent);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TypeSafeClient.Endpoint, request.RequestUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Key, request.Headers.Authorization?.Parameter);

        JsonNode json = JsonNode.Parse(body)!;
        Assert.Equal("jev-1.13.0", json["model"]!.GetValue<string>());
        Assert.Equal("Maya Ortiz", json["state"]!["candidates"]![0]!["word"]!.GetValue<string>());
        Assert.Equal("Join the Zoom call.", json["state"]!["candidates"]![1]!["example"]!.GetValue<string>());
        JsonObject questions = json["questions"]!.AsObject();
        Assert.Equal(["c0", "c1"], questions.Select(q => q.Key));
        Assert.All(questions, q => Assert.Equal("noul", q.Value!["type"]!.GetValue<string>()));
        Assert.Contains("candidates[1].word", questions["c1"]!["instructions"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheAnswers_AreEachCandidatesProbability()
    {
        Answer(HttpStatusCode.OK, """{"answers":{"c0":{"type":"noul","noul":0.93},"c1":{"type":"noul","noul":0.12}}}""");

        TypeSafeJudgement judgement = await _client.JudgeAsync(Key, Candidates, CancellationToken.None);

        Assert.Equal(TypeSafeOutcome.Answered, judgement.Outcome);
        Assert.Equal(0.93, judgement.Nouls["Maya Ortiz"], 3);
        Assert.Equal(0.12, judgement.Nouls["Zoom"], 3);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARefusedKey_SaysSo(HttpStatusCode status)
    {
        Answer(status, """{"error":"invalid key"}""");

        Assert.Equal(TypeSafeOutcome.KeyRefused, (await _client.JudgeAsync(Key, Candidates, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task BusyOrRateLimited_IsRetriedTwiceWithGrowingWaits_ThenGivesUp()
    {
        Answer((HttpStatusCode)529, "{}");
        Answer(HttpStatusCode.TooManyRequests, "{}");
        Answer((HttpStatusCode)529, "{}");

        TypeSafeJudgement judgement = await _client.JudgeAsync(Key, Candidates, CancellationToken.None);

        Assert.Equal(TypeSafeOutcome.Unavailable, judgement.Outcome);
        Assert.Equal(3, _sent.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], _waits);
    }

    [Fact]
    public async Task ARetryThatSucceeds_IsAnswered()
    {
        Answer(HttpStatusCode.TooManyRequests, "{}");
        Answer(HttpStatusCode.OK, """{"answers":{"c0":{"type":"noul","noul":0.8},"c1":{"type":"noul","noul":0.1}}}""");

        Assert.Equal(TypeSafeOutcome.Answered, (await _client.JudgeAsync(Key, Candidates, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task NoInternetOrAnOddAnswer_IsUnavailable()
    {
        Assert.Equal(TypeSafeOutcome.Unavailable, (await _client.JudgeAsync(Key, Candidates, CancellationToken.None)).Outcome);

        Answer(HttpStatusCode.OK, "not json");
        Assert.Equal(TypeSafeOutcome.Unavailable, (await _client.JudgeAsync(Key, Candidates, CancellationToken.None)).Outcome);

        Answer(HttpStatusCode.OK, """{"answers":{"c0":{"type":"noul","noul":0.8}}}""");
        Assert.Equal(TypeSafeOutcome.Unavailable, (await _client.JudgeAsync(Key, Candidates, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task CheckingAKey_SendsNoDictationText()
    {
        Answer(HttpStatusCode.OK, """{"answers":{"k":{"type":"noul","noul":0.9}}}""");

        Assert.Equal(TypeSafeOutcome.Answered, await _client.CheckKeyAsync(Key, CancellationToken.None));
        Assert.DoesNotContain("candidates", Assert.Single(_sent).Body);
    }

    [Fact]
    public async Task NoCandidates_SendsNothing()
    {
        TypeSafeJudgement judgement = await _client.JudgeAsync(Key, [], CancellationToken.None);

        Assert.Equal(TypeSafeOutcome.Answered, judgement.Outcome);
        Assert.Empty(_sent);
    }

    private void Answer(HttpStatusCode status, string body) =>
        _answers.Enqueue(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private sealed class FakeHandler(TypeSafeClientTests test) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            test._sent.Add((request, body));
            return test._answers.Count > 0 ? test._answers.Dequeue() : throw new HttpRequestException("No internet");
        }
    }
}
