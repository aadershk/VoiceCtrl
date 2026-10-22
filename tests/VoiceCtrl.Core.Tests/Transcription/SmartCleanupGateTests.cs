using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class SmartCleanupGateTests
{
    private static PromptContext Context(string level = "standard", string? formatting = null, bool email = false) =>
        new(level, null, formatting, null, email);

    [Theory]
    [InlineData("Sounds good.")]
    [InlineData("Yes, please send it.")]
    public void FourWordsOrFewer_SkipTheModel(string text) =>
        Assert.False(SmartCleanupGate.ShouldRun(text, Context("aggressive", email: true)));

    [Fact]
    public void WordForWordWithNothingToLayOut_SkipsTheModel() =>
        Assert.False(SmartCleanupGate.ShouldRun("So we need to ship the report on Friday.", Context("verbatim")));

    [Fact]
    public void AFluentSentence_AtStandard_SkipsTheModel() =>
        Assert.False(SmartCleanupGate.ShouldRun("We need to ship the report on Friday afternoon.", Context()));

    // ---- Ticket 55

    private const string LongFluentText =
        "The pilot ran for six weeks with forty users, and most of them used the app every day. Feedback on the search " +
        "was very positive. The export is still slow on large files and two users lost their drafts after an update.";

    [Fact]
    public void ALongerTextInADocument_ReachesTheModelForItsParagraphs() =>
        Assert.True(SmartCleanupGate.ShouldRun(LongFluentText, Context(formatting: FormattingHintMapper.For(AppKind.Document))));

    [Fact]
    public void AShortFluentSentenceInADocument_SkipsTheModel() =>
        Assert.False(SmartCleanupGate.ShouldRun("We need to ship the report on Friday afternoon.", Context(formatting: FormattingHintMapper.For(AppKind.Document))));

    [Fact]
    public void ALongerTextElsewhere_StillSkipsTheModel() =>
        Assert.False(SmartCleanupGate.ShouldRun(LongFluentText, Context()));

    [Theory]
    [InlineData("So, quick update on the launch. The build is done.")]
    [InlineData("Okay so I need you to refactor the user service.")]
    [InlineData("Well, the build is done and testing starts Monday.")]
    public void AnOpeningFiller_ReachesTheModel(string text) =>
        Assert.True(SmartCleanupGate.ShouldRun(text, Context()));

    [Theory]
    [InlineData("Send it to John, actually, make that Sarah, by Friday.")]
    [InlineData("Wait no, the meeting is at four on Friday.")]
    [InlineData("First buy milk, second call the bank, third book flights.")]
    [InlineData("Add bullet point milk and bullet point eggs to the list.")]
    [InlineData("It was, like, you know, a really long day for everyone.")]
    [InlineData("The meeting is at 3, wait, 4 PM in the main room.")]
    [InlineData("Things to pack: one, passport, two, charger, three, sunscreen.")]
    [InlineData("The meeting is at three thirty and the budget is twenty five thousand dollars.")]
    [InlineData("We shipped units in the first quarter, up ten percent on last year.")]
    public void RetractionsListsAndFillers_RunTheModel(string text) =>
        Assert.True(SmartCleanupGate.ShouldRun(text, Context()));

    [Theory]
    [InlineData("Three things for my interview, one go through the job description, two scrape details, three prepare.")]
    [InlineData("Two things I need from you, one the signed contract, two the updated budget.")]
    [InlineData("We have four reasons to switch and I want to go through them with you today.")]
    public void BareNumeralsAndCountedLeadIns_RunTheModel(string text) =>
        Assert.True(SmartCleanupGate.ShouldRun(text, Context()));

    [Fact]
    public void AnEnumeration_RunsTheModelInAChatApp() =>
        Assert.True(SmartCleanupGate.ShouldRun(
            "Three things for my interview, one go through the job description, two scrape details, three prepare.",
            Context(formatting: FormattingHintMapper.ResolveExplicit(AppProfile.Prose))));

    // Ticket 55: the greeting and sign-off lines, and a chat's lines, are set in code (PasteLayout),
    // so a short email or chat message with nothing else to do no longer waits for the model.

    [Fact]
    public void AShortEmail_IsLaidOutInCodeAlone() =>
        Assert.False(SmartCleanupGate.ShouldRun("Hi Sarah, can we move the call to Friday? Thanks, Alex.", Context(email: true)));

    [Fact]
    public void ALongerEmail_ReachesTheModelForItsParagraphs() =>
        Assert.True(SmartCleanupGate.ShouldRun(LongFluentText, Context(email: true)));

    [Fact]
    public void AShortChatMessage_IsLaidOutInCodeAlone() =>
        Assert.False(SmartCleanupGate.ShouldRun(
            "Sounds good. I will be there in five minutes.", Context(formatting: FormattingHintMapper.ResolveExplicit(AppProfile.Prose))));

    [Fact]
    public void AnAiPrompt_AlwaysReachesTheModel_ForTheItemsItMayList() =>
        Assert.True(SmartCleanupGate.ShouldRun(
            "We need to ship the report on Friday afternoon.", Context(formatting: FormattingHintMapper.For(AppKind.AiPrompt))));

    [Fact]
    public void Aggressive_RunsTheModel() =>
        Assert.True(SmartCleanupGate.ShouldRun("We need to ship the report on Friday afternoon.", Context("aggressive")));

    [Fact]
    public void AStructuredFormattingHint_RunsTheModel() =>
        Assert.True(SmartCleanupGate.ShouldRun(
            "We need to ship the report on Friday afternoon.", Context(formatting: "Use bullet points for lists.")));
}
