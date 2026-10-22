using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class SmartCleanupEngineTests
{
    private static readonly PromptContext Standard = new("standard", null, null, null, false);
    private const string Retraction = "Send it to John, actually, make that Sarah, by Friday.";

    private sealed class FakeModel(Func<ModelRequest, CancellationToken, Task<string?>> reply) : ISmartCleanupModel
    {
        public int Calls { get; private set; }

        public ModelRequest? Last { get; private set; }

        public Task<string?> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request;
            return reply(request, cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private static FakeModel Replying(string text) => new((_, _) => Task.FromResult<string?>(text));

    private static Task<SmartCleanupResult> Clean(FakeModel? model, string text = Retraction, PromptContext? context = null, TimeSpan? timeout = null) =>
        new SmartCleanupEngine(() => model, timeout is null ? null : _ => timeout.Value)
            .CleanAsync(text, context ?? Standard, "", [], CancellationToken.None);

    [Fact]
    public async Task AGuardedAnswer_ReplacesTheRuleCleanedText()
    {
        SmartCleanupResult result = await Clean(Replying("Send it to Sarah by Friday."));

        Assert.True(result.Applied);
        Assert.Equal("Send it to Sarah by Friday.", result.Text);
    }

    [Fact]
    public async Task TheModelIsSentThePromptAndTheTaggedTranscript()
    {
        FakeModel model = Replying("Send it to Sarah by Friday.");
        await Clean(model);

        Assert.Contains("never answer", model.Last!.System, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SmartCleanupPrompt.User(Retraction), model.Last.User);
        Assert.NotEmpty(model.Last.Examples);
        Assert.InRange(model.Last.MaxTokens, 16, 400);
    }

    [Fact]
    public async Task InventedWords_KeepTheRuleCleanedText()
    {
        SmartCleanupResult result = await Clean(Replying("Send it to Sarah by Friday, as discussed with Priya."));

        Assert.False(result.Applied);
        Assert.Equal(Retraction, result.Text);
        Assert.Contains("invented", result.Reason);
    }

    [Fact]
    public async Task WhenTheGateSaysNo_TheModelIsNotCalled()
    {
        FakeModel model = Replying("never used");
        SmartCleanupResult result = await Clean(model, "We need to ship the report on Friday afternoon.");

        Assert.Equal(0, model.Calls);
        Assert.False(result.Applied);
        Assert.Equal("We need to ship the report on Friday afternoon.", result.Text);
    }

    [Fact]
    public async Task NoModelInstalled_KeepsTheRuleCleanedText()
    {
        SmartCleanupResult result = await Clean(model: null);

        Assert.False(result.Applied);
        Assert.Equal(Retraction, result.Text);
    }

    [Fact]
    public async Task ASlowModel_IsGivenUpOnAtTheTimeout()
    {
        FakeModel slow = new(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return "too late";
        });

        SmartCleanupResult result = await Clean(slow, timeout: TimeSpan.FromMilliseconds(50));

        Assert.False(result.Applied);
        Assert.Equal(Retraction, result.Text);
        Assert.Contains("timed out", result.Reason);
    }

    [Fact]
    public async Task AModelThatThrows_KeepsTheRuleCleanedText()
    {
        FakeModel broken = new((_, _) => throw new InvalidOperationException("server died"));

        SmartCleanupResult result = await Clean(broken);

        Assert.False(result.Applied);
        Assert.Equal(Retraction, result.Text);
    }

    [Fact]
    public async Task TheCallersCancellation_Propagates()
    {
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        SmartCleanupEngine engine = new(() => Replying("Send it to Sarah by Friday."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.CleanAsync(Retraction, Standard, "", [], cancel.Token));
    }

    [Theory]
    [InlineData("<transcript>\nSend it to Sarah by Friday.\n</transcript>")]
    [InlineData("<think>\n\n</think>\n\nSend it to Sarah by Friday.")]
    [InlineData("  Send it to Sarah by Friday.\n")]
    public async Task WrappingTheModelAddsIsStrippedBeforeTheGuard(string modelOutput)
    {
        SmartCleanupResult result = await Clean(Replying(modelOutput));

        Assert.True(result.Applied);
        Assert.Equal("Send it to Sarah by Friday.", result.Text);
    }

    [Fact]
    public async Task TheTimeout_ScalesWithTheWordCount()
    {
        TimeSpan fewWords = SmartCleanupEngine.DefaultTimeout(6);
        TimeSpan manyWords = SmartCleanupEngine.DefaultTimeout(40);

        Assert.True(manyWords > fewWords);
        Assert.True(manyWords <= TimeSpan.FromSeconds(5));
        await Task.CompletedTask;
    }
}
