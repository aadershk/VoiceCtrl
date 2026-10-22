using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class SmartCleanupGuardTests
{
    private static readonly string[] NoTerms = [];

    private static bool Accepts(string raw, string output, string level = "standard", string[]? terms = null) =>
        SmartCleanupGuard.Check(raw, output, level, terms ?? NoTerms).Accepted;

    [Fact]
    public void UnchangedWords_AreAccepted_WhateverTheCaseAndPunctuation() =>
        Assert.True(Accepts("send the report to john tomorrow", "Send the report to John tomorrow."));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOutput_IsRejected(string output) =>
        Assert.False(Accepts("send the report", output));

    [Theory]
    [InlineData("Here is the cleaned text: Send the report.")]
    [InlineData("Here's your text: Send the report.")]
    [InlineData("```Send the report.```")]
    public void PreambleAndFences_AreRejected(string output) =>
        Assert.False(Accepts("send the report", output));

    [Fact]
    public void Standard_AcceptsFillerAndRepeatRemoval() =>
        Assert.True(Accepts("so um we we need to uh ship it today", "So we need to ship it today."));

    // ---- Ticket 56: the Formal Style's word swaps, made by the model itself

    [Theory]
    [InlineData("Yeah sounds good, I'll be there in like five minutes.", "Yes, sounds good. I will be there in five minutes.")]
    [InlineData("It's a tight timeline, I'm gonna call them.", "It is a tight timeline. I am going to call them.")]
    [InlineData("Nope, we kinda need it.", "No, we kind of need it.")]
    public void FormalWordSwaps_AreNotInventedWords(string raw, string output) =>
        Assert.True(Accepts(raw, output));

    [Fact]
    public void AnInventedWord_IsStillRejected_InFormalWording() =>
        Assert.False(Accepts("yeah sounds good", "Yes, that sounds very good."));

    // ---- Ticket 55: the per-app layouts

    [Fact]
    public void TheAndBeforeAListsLastOrdinal_GoesWithTheLayout() =>
        Assert.True(Accepts(
            "Can you do three things for me. First update the page, second fix the link, and third send the report.",
            "Can you do three things for me:\n1. Update the page\n2. Fix the link\n3. Send the report"));

    [Fact]
    public void ASpokenHeadingCommand_GoesWithTheLayout() =>
        Assert.True(Accepts(
            "Heading project goals.\n\nOur main goal is to cut onboarding time.",
            "Project goals\n\nOur main goal is to cut onboarding time."));

    [Theory]
    [InlineData("Okay so I need you to refactor the user service.", "I need you to refactor the user service.")]
    [InlineData("Alright, the build is done.", "The build is done.")]
    public void AnOpeningOkay_IsAFiller(string raw, string output) =>
        Assert.True(Accepts(raw, output));

    [Fact]
    public void Standard_RejectsAnInventedContentWord() =>
        Assert.False(Accepts("we need to ship it today", "We urgently need to ship it today."));

    [Fact]
    public void Standard_RejectsDroppedContentWordsWithoutARetraction() =>
        Assert.False(Accepts("we need to ship the new billing page today", "We need to ship it today."));

    [Fact]
    public void Standard_AcceptsARetractedSpan() =>
        Assert.True(Accepts("send it to john actually no make that sarah", "Send it to Sarah."));

    [Fact]
    public void Standard_AcceptsAtMostTwoFunctionWordInsertions() =>
        Assert.True(Accepts("ship it today", "Ship it to the today."));

    [Fact]
    public void Standard_RejectsMoreThanTwoInsertions() =>
        Assert.False(Accepts("ship it today", "Ship it to the a today."));

    [Fact]
    public void Verbatim_RejectsFillerRemoval() =>
        Assert.False(Accepts("so um we need it", "So we need it.", "verbatim"));

    [Fact]
    public void Verbatim_AcceptsCaseAndPunctuationOnly() =>
        Assert.True(Accepts("so um we need it", "So, um, we need it.", "verbatim"));

    [Fact]
    public void Numerals_AreAcceptedWhenNumberWordsWereSpoken() =>
        Assert.True(Accepts("the meeting is at three thirty on the fifth", "The meeting is at 3:30 on the 5th.", "verbatim"));

    [Fact]
    public void Digits_AreRejectedWhenNothingNumericWasSpoken() =>
        Assert.False(Accepts("the meeting is tomorrow", "The meeting is at 3 tomorrow."));

    [Fact]
    public void ListMarkers_BecomeNumbering() =>
        Assert.True(Accepts(
            "first buy milk second call the bank third book flights",
            "1. Buy milk\n2. Call the bank\n3. Book flights"));

    [Fact]
    public void ADictionaryTerm_MayReplaceTheWordsItWasHeardAs() =>
        Assert.True(Accepts("open the blue peak dashboard", "Open the Bluepeak dashboard.", terms: ["Bluepeak"]));

    [Fact]
    public void ADictionaryTerm_IsNotAllowedToAppearFromNowhere() =>
        Assert.False(Accepts("open the dashboard", "Open the Bluepeak dashboard.", terms: ["Bluepeak"]));

    [Fact]
    public void AnsweringTheDictation_IsRejected() =>
        Assert.False(Accepts("what is the capital of france", "The capital of France is Paris."));

    [Fact]
    public void ADictatedQuestion_StaysAQuestion() =>
        Assert.True(Accepts("what is the capital of france", "What is the capital of France?"));

    [Fact]
    public void EmailLayout_CannotAddAGreetingNobodySaid() =>
        Assert.False(Accepts("can we move the call to friday thanks", "Hi,\n\nCan we move the call to Friday?\n\nThanks,\nAlex"));

    [Fact]
    public void EmailLayout_MayMoveSpokenWordsIntoShape() =>
        Assert.True(Accepts("hi sarah can we move the call to friday thanks", "Hi Sarah,\n\nCan we move the call to Friday?\n\nThanks"));

    [Fact]
    public void Aggressive_AcceptsTighteningWithinTheLengthRatio() =>
        Assert.True(Accepts(
            "so basically the report is pretty much done now",
            "The report is done now.",
            "aggressive"));

    [Fact]
    public void Aggressive_RejectsCollapsingMostOfTheText() =>
        Assert.False(Accepts(
            "i just wanted to let you know that the report is basically done and ready",
            "Done.",
            "aggressive"));

    [Fact]
    public void Aggressive_RejectsDroppingAName() =>
        Assert.False(Accepts(
            "I spoke with Priya about the budget and she agreed with the plan",
            "I spoke about the budget and she agreed with the plan.",
            "aggressive"));

    [Fact]
    public void Standard_AcceptsAFalseStartThatTheSpeakerRestarted() =>
        Assert.True(Accepts("most of it was m most of it made sense but not all", "Most of it made sense, but not all."));

    [Fact]
    public void Standard_AcceptsAListsAndBecomingAComma() =>
        Assert.True(Accepts("send it to ann and bob and carol today", "Send it to Ann, Bob, and Carol today."));

    [Fact]
    public void Standard_AcceptsWait_AsARetraction() =>
        Assert.True(Accepts("the meeting is at 3 wait 4 pm in the main room", "The meeting is at 4 PM in the main room."));

    [Fact]
    public void AContractionIsTheSameWordsSpelledShort() =>
        Assert.True(Accepts("i have restructured it and i will send it", "I've restructured it and I'll send it."));

    [Fact]
    public void Standard_AcceptsMendingAWordTheRecogniserGarbled() =>
        Assert.True(Accepts("then we run refuctation tests on it", "Then we run refutation tests on it."));

    [Fact]
    public void Verbatim_DoesNotMendAnything() =>
        Assert.False(Accepts("then we run refuctation tests on it", "Then we run refutation tests on it.", "verbatim"));

    [Fact]
    public void Standard_RejectsSwappingARealWordForAnother() =>
        Assert.False(Accepts("the budget is for the harbor project", "The budget is for the garden project."));

    [Fact]
    public void Standard_RejectsMoreThanThreeMendedWords() =>
        Assert.False(Accepts(
            "the refuctation of those hipotheses and the interprettation of convergense are pending",
            "The refutation of those hypotheses and the interpretation of convergence are pending."));

    [Fact]
    public void ADictionaryTerm_IsMatchedThroughItsAlsoHeardAs()
    {
        DictionaryEntry kubernetes = new("Kubernetes", ["cooper netties"]);

        GuardResult result = SmartCleanupGuard.Check(
            "check the cooper netties cluster for errors", "Check the Kubernetes cluster for errors.", "standard", [kubernetes]);

        Assert.True(result.Accepted, result.Reason);
    }

    [Fact]
    public void ADictionaryTerm_IsFoundInsideALongerDroppedSpan() =>
        Assert.True(Accepts("send the notes to adarsh by the end of the day actually by noon", "Send the notes to Aadersh by noon.", terms: ["Aadersh"]));

    [Fact]
    public void Standard_AcceptsANameSwapMadeByARetraction() =>
        Assert.True(Accepts(
            "tell daniel the invoice is paid actually tell elena she handles the accounts",
            "Tell Elena the invoice is paid, she handles the accounts."));

    [Fact]
    public void Standard_AcceptsKindOfAsAHedge() =>
        Assert.True(Accepts("i was kind of hoping that we could talk", "I was hoping that we could talk."));

    [Fact]
    public void Standard_StillRejectsADroppedWordFarFromAnyRetraction() =>
        Assert.False(Accepts(
            "send the signed contract to the lawyer today and then file the invoice tomorrow actually friday",
            "Send the contract to the lawyer today and then file the invoice Friday."));

    [Fact]
    public void ARejection_SaysWhy()
    {
        GuardResult result = SmartCleanupGuard.Check("ship it", "Ship it immediately", "standard", NoTerms);
        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
