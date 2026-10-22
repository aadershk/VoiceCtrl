using System.Net;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.EmailRequests;

/// <summary>When an Email request is written, by which model, and what comes back (ticket 57).</summary>
public class EmailWriterTests
{
    private const string Request = "Write an email to Mark thanking him for his time.";

    private sealed class FakeBodyWriter(bool ready = true) : IEmailBodyWriter
    {
        public bool IsReady { get; set; } = ready;

        public string? Reply { get; set; } = "Thank you for your time. I really appreciate it.";

        public Exception? Throws { get; set; }

        public List<EmailRequest> Calls { get; } = [];

        public int Prewarms { get; private set; }

        public Task<string?> WriteBodyAsync(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Throws is null ? Task.FromResult(Reply) : Task.FromException<string?>(Throws);
        }

        public Task PrewarmAsync(IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
        {
            Prewarms++;
            return Task.CompletedTask;
        }
    }

    private sealed class Setup
    {
        public string App { get; set; } = "outlook";
        public string Cleanup { get; set; } = CleanupLevels.Standard;
        public AppProfile? Profile { get; set; }
        public TranscriptionModePreference Mode { get; set; } = TranscriptionModePreference.Offline;
        public StyleSettings Styles { get; set; } = new() { YourName = "Alex Kim" };
        public FakeBodyWriter Online { get; } = new();
        public FakeBodyWriter Offline { get; } = new();
        public ApiKeyHealth KeyHealth { get; } = new();
        public bool Network { get; set; } = true;

        public EmailWriter Writer() => new(
            () => GeminiPrompt.ContextFor(Profile, App, new PromptDefaults(Cleanup, ToneAwareness: true, [], [], Styles: Styles)),
            () => Styles, PersonalizationStore.Empty, () => Mode, Online, Offline, KeyHealth, () => Network);
    }

    [Fact]
    public void ARequestInAnEmailApp_IsRecognised_WithTheAppsStyleAndYourName()
    {
        EmailWriter writer = new Setup().Writer();
        EmailRequest? request = writer.ForInstruction(Request);

        Assert.True(writer.AppliesHere());
        Assert.NotNull(request);
        Assert.Equal(Style.Formal, request.Style);
        Assert.Equal("Alex Kim", request.YourName);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("slack")]
    [InlineData("winword")]
    [InlineData("code")]
    public void OutsideAnEmailApp_ItDoesNotApply(string app)
    {
        EmailWriter writer = new Setup { App = app }.Writer();

        Assert.False(writer.AppliesHere());
        Assert.Null(writer.ForInstruction(Request));
    }

    [Fact]
    public void AProfileTurningEmailLayoutOff_TurnsItOff() =>
        Assert.False(new Setup { Profile = new AppProfile { App = "outlook", EmailLayout = false } }.Writer().AppliesHere());

    [Fact]
    public void OfflineWithoutTheLanguageModel_ItDoesNotApply()
    {
        var setup = new Setup();
        setup.Offline.IsReady = false;

        Assert.False(setup.Writer().AppliesHere());
    }

    [Fact]
    public void AnEdit_IsNotARequest() =>
        Assert.Null(new Setup().Writer().ForInstruction("make it shorter"));

    [Fact]
    public async Task Offline_TheLocalModelWritesIt_AndCodeSetsGreetingAndSignOff()
    {
        var setup = new Setup();
        EmailWriter writer = setup.Writer();

        string? email = await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None);

        Assert.Equal("Dear Mark,\n\nThank you for your time. I really appreciate it.\n\nKind regards,\nAlex Kim", email);
        Assert.Single(setup.Offline.Calls);
        Assert.Empty(setup.Online.Calls);
    }

    [Fact]
    public async Task TheEmailAppsStyle_IsApplied()
    {
        var setup = new Setup { Styles = new StyleSettings { Email = Style.Casual, YourName = "Alex" } };
        setup.Offline.Reply = "I can't thank you enough. It was great.";
        EmailWriter writer = setup.Writer();

        string? email = await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None);

        // Casual: no closing full stop on a line; the contraction stays.
        Assert.Equal("Hi Mark,\n\nI can't thank you enough. It was great\n\nBest,\nAlex", email);
    }

    [Fact]
    public async Task Online_GeminiWritesIt()
    {
        var setup = new Setup { Mode = TranscriptionModePreference.Online };
        EmailWriter writer = setup.Writer();

        Assert.NotNull(await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None));
        Assert.Single(setup.Online.Calls);
        Assert.Empty(setup.Offline.Calls);
    }

    [Fact]
    public async Task Auto_FallsBackToTheLocalModel_WhenGeminiIsBusy()
    {
        var setup = new Setup { Mode = TranscriptionModePreference.Auto };
        setup.Online.Throws = new OnlineServiceException(HttpStatusCode.ServiceUnavailable, "busy");
        EmailWriter writer = setup.Writer();

        Assert.NotNull(await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None));
        Assert.Single(setup.Offline.Calls);
    }

    [Fact]
    public async Task Online_DoesNotFallBack_AndTypesTheRequestAsSaid()
    {
        var setup = new Setup { Mode = TranscriptionModePreference.Online };
        setup.Online.Throws = new OnlineServiceException(HttpStatusCode.ServiceUnavailable, "busy");
        EmailWriter writer = setup.Writer();

        Assert.Null(await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None));
        Assert.Empty(setup.Offline.Calls);
    }

    [Fact]
    public async Task Auto_WithoutANetwork_UsesTheLocalModel()
    {
        var setup = new Setup { Mode = TranscriptionModePreference.Auto, Network = false };
        EmailWriter writer = setup.Writer();

        Assert.NotNull(await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None));
        Assert.Empty(setup.Online.Calls);
    }

    [Theory]
    [InlineData("Thank you for meeting me on Friday.")]
    [InlineData("I'm unable to help with that, as an AI.")]
    [InlineData(null)]
    public async Task ABodyTheGuardRefuses_OrNoBody_IsNoEmail(string? reply)
    {
        var setup = new Setup();
        setup.Offline.Reply = reply;
        EmailWriter writer = setup.Writer();

        Assert.Null(await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None));
    }

    [Fact]
    public async Task AFailingModel_IsNoEmail_NotAnError()
    {
        var setup = new Setup();
        setup.Offline.Throws = new InvalidOperationException("crashed");
        EmailWriter writer = setup.Writer();

        Assert.Null(await writer.WriteAsync(writer.ForInstruction(Request)!, CancellationToken.None));
    }

    [Fact]
    public async Task Prewarm_PrimesTheWriter_OnlyInAnEmailApp()
    {
        var inOutlook = new Setup();
        var inSlack = new Setup { App = "slack" };

        await inOutlook.Writer().PrewarmAsync();
        await inSlack.Writer().PrewarmAsync();

        Assert.Equal(1, inOutlook.Offline.Prewarms);
        Assert.Equal(0, inSlack.Offline.Prewarms);
    }
}
