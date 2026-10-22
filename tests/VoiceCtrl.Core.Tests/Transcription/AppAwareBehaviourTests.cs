using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>Slice 38: the same app-aware behaviours Online and Offline (category defaults, the chat
/// full stop, two-item lists, the line phrases) and the Original Online keeps.</summary>
public class AppAwareBehaviourTests
{
    private static readonly PromptDefaults Defaults = new(CleanupLevels.Standard, ToneAwareness: true, [], []);

    // ---- Category defaults for apps with no entry of their own

    [Theory]
    [InlineData("telegram")]
    [InlineData("signal")]
    [InlineData("ms-teams")]
    [InlineData("msteams")]
    [InlineData("teams")]
    [InlineData("messenger")]
    [InlineData("skype")]
    public void AnUnlistedChatApp_GetsTheChatDefaults(string processName)
    {
        Assert.Contains("casual", GeminiPrompt.ContextFor(null, processName, Defaults).ToneHint);
        Assert.True(FormattingHintMapper.IsProse(FormattingHintMapper.Resolve(processName)));
    }

    [Theory]
    [InlineData("olk")]
    [InlineData("hxoutlook")]
    [InlineData("thunderbird")]
    [InlineData("mailspring")]
    public void AnUnlistedEmailApp_GetsTheEmailTone(string processName)
    {
        Assert.Contains("professional", GeminiPrompt.ContextFor(null, processName, Defaults).ToneHint);
    }

    [Fact]
    public void ABuiltInChatAppProfile_StartsAsProse()
    {
        Assert.Equal(AppProfile.Prose, FormattingHintMapper.BuiltIn("telegram"));
    }

    // ---- Chat lines and Style

    [Theory]
    [InlineData("slack", "Paragraphs")]
    [InlineData("telegram", "OneParagraph")]
    [InlineData("whatsapp", "OneParagraph")]
    [InlineData("windowsterminal", "None")]
    [InlineData("outlook", "None")]
    [InlineData("notepad", "None")]
    public void TheChatLayout_FollowsTheAppsKind(string processName, string expected)
    {
        Assert.Equal(Enum.Parse<ChatLayout>(expected), GeminiPrompt.ContextFor(null, processName, Defaults).Chat);
    }

    [Theory]
    [InlineData("slack", Style.Casual)]
    [InlineData("whatsapp", Style.Casual)]
    [InlineData("outlook", Style.Formal)]
    [InlineData("winword", Style.Formal)]
    [InlineData("notepad", Style.Formal)]
    [InlineData("cursor", null)]
    public void AFreshInstall_GivesEachKindItsDefaultStyle(string processName, Style? expected)
    {
        Assert.Equal(expected, GeminiPrompt.ContextFor(null, processName, Defaults).Style);
    }

    [Fact]
    public void AnAppsProfileStyle_WinsOverItsKinds()
    {
        PromptContext context = GeminiPrompt.ContextFor(new AppProfile { Style = "formal" }, "slack", Defaults);

        Assert.Equal(Style.Formal, context.Style);
        Assert.Equal(ChatLayout.Paragraphs, context.Chat);
    }

    [Fact]
    public void AProfileThatAsksForProse_GivesAnyAppAChatMessagesParagraph()
    {
        PromptContext context = GeminiPrompt.ContextFor(new AppProfile { Formatting = AppProfile.Prose }, "notepad", Defaults);

        Assert.Equal(ChatLayout.OneParagraph, context.Chat);
    }

    // Ticket 55 (owner, 2026-10-19): no line of a casual message ends in a full stop.
    [Theory]
    [InlineData("Sounds good, see you at five.", "Sounds good, see you at five")]
    [InlineData("On my way.", "On my way")]
    [InlineData("Sounds good. See you at five.", "Sounds good. See you at five")]
    [InlineData("Is that ok?", "Is that ok?")]
    [InlineData("Wait...", "Wait...")]
    [InlineData("Line one.\nLine two.", "Line one\nLine two")]
    [InlineData("Ask Dr. Smith.", "Ask Dr. Smith")]
    [InlineData("Ask Mr.", "Ask Mr.")]
    [InlineData("It costs 3.50.", "It costs 3.50")]
    [InlineData("", "")]
    public void TheFullStopClosingEachChatLine_IsDropped(string text, string expected)
    {
        Assert.Equal(expected, CasualChatPunctuation.Apply(text));
    }

    // ---- Two-item lists and line phrases reach both prompts

    [Fact]
    public void ListsStartAtTwoItems_InBothPrompts()
    {
        PromptContext context = GeminiPrompt.ContextFor(null, "notepad", Defaults);

        Assert.Contains("two or more items", GeminiPrompt.Build(context, string.Empty, []));
        Assert.Contains("two or more", SmartCleanupPrompt.System(context.CleanupLevel, string.Empty));
    }

    [Fact]
    public void TheLineAndParagraphPhrases_AreNamedInBothPrompts()
    {
        PromptContext context = GeminiPrompt.ContextFor(null, "notepad", Defaults);

        foreach (string prompt in new[] { GeminiPrompt.Build(context, string.Empty, []), SmartCleanupPrompt.System(context.CleanupLevel, string.Empty) })
        {
            Assert.Contains("new line", prompt);
            Assert.Contains("next line", prompt);
            Assert.Contains("line break", prompt);
            Assert.Contains("skip a line", prompt);
            Assert.Contains("new paragraph", prompt);
        }
    }

    [Fact]
    public void ATwoItemListExample_IsShownToTheLocalModel()
    {
        Assert.Contains(
            SmartCleanupPrompt.ExamplesFor(CleanupLevels.Standard),
            example => example.Assistant.Contains("1. ") && !example.Assistant.Contains("3. "));
    }

    // ---- Online keeps the Original

    [Fact]
    public void GeminiAnswerWithoutAnOriginal_IsJustTheText()
    {
        (string text, string? original) = GeminiOutput.Split("Send it to Sarah.");

        Assert.Equal("Send it to Sarah.", text);
        Assert.Null(original);
    }

    [Fact]
    public void GeminiAnswerWithAnOriginal_IsSplitAtTheMarker()
    {
        (string text, string? original) = GeminiOutput.Split(
            "Send it to Sarah.\n[ORIGINAL_TRANSCRIPT]\nSend it to John, actually, make that Sarah.");

        Assert.Equal("Send it to Sarah.", text);
        Assert.Equal("Send it to John, actually, make that Sarah.", original);
    }

    [Fact]
    public void AnOriginalThatOnlyDiffersInPunctuationAndCase_IsNotKept()
    {
        (_, string? original) = GeminiOutput.Split("Send it to Sarah.\n[ORIGINAL_TRANSCRIPT]\nsend it to sarah");

        Assert.Null(original);
    }

    [Fact]
    public void AMarkerWithNoTextBeforeIt_KeepsTheTextAfterIt()
    {
        (string text, string? original) = GeminiOutput.Split("[ORIGINAL_TRANSCRIPT]\nSend it to Sarah.");

        Assert.Equal("Send it to Sarah.", text);
        Assert.Null(original);
    }

    [Fact]
    public void AnOriginalFarLongerThanTheText_IsNotKept()
    {
        string runaway = string.Join(' ', Enumerable.Repeat("word", 400));

        (_, string? original) = GeminiOutput.Split($"Ok.\n[ORIGINAL_TRANSCRIPT]\n{runaway}");

        Assert.Null(original);
    }

    [Fact]
    public void TheGeminiPrompt_AsksForTheOriginalOnlyWhenItWasEdited()
    {
        string prompt = GeminiPrompt.Build(GeminiPrompt.ContextFor(null, "notepad", Defaults), string.Empty, []);

        Assert.Contains("[ORIGINAL_TRANSCRIPT]", prompt);
    }
}

public class ListLeadInTests
{
    [Theory]
    [InlineData("Two changes, first rename the helper, second add a test.", "1. Rename the helper\n2. Add a test", "Two changes:\n1. Rename the helper\n2. Add a test")]
    [InlineData("Groceries are first milk, second eggs.", "1. Milk\n2. Eggs", "Groceries are:\n1. Milk\n2. Eggs")]
    public void AListThatLostItsLeadIn_GetsItBack(string input, string answer, string expected)
    {
        Assert.Equal(expected, ListLeadIn.Restore(input, answer));
    }

    // Ticket 55: a lead-in said as its own sentence ("... for me. First ...") is the speaker's too.
    [Theory]
    [InlineData(
        "Can you do three things for me. First update the page, second fix the link, and third send the report.",
        "1. Update the page\n2. Fix the link\n3. Send the report",
        "Can you do three things for me:\n1. Update the page\n2. Fix the link\n3. Send the report")]
    [InlineData(
        "Can you do three things for me. First update the page, second fix the link, and third send the report.",
        "Three things for me:\n1. Update the page\n2. Fix the link\n3. Send the report",
        "Can you do three things for me:\n1. Update the page\n2. Fix the link\n3. Send the report")]
    [InlineData(
        "The build is done. Can you do two things. First update the page, second send the report.",
        "The build is done.\nTwo things:\n1. Update the page\n2. Send the report",
        "The build is done.\nCan you do two things:\n1. Update the page\n2. Send the report")]
    public void ALeadInSentence_ThatTheModelShortened_IsTheSpeakersAgain(string input, string answer, string expected)
    {
        Assert.Equal(expected, ListLeadIn.Restore(input, answer));
    }

    [Theory]
    [InlineData("Two changes, first rename the helper, second add a test.", "Two changes:\n1. Rename the helper\n2. Add a test")]
    [InlineData("Quick update. Can you do two things. First X, second Y.", "Quick update. Can you do two things:\n1. X\n2. Y")]
    [InlineData("First rename the helper, second add a test.", "1. Rename the helper\n2. Add a test")]
    [InlineData("I did it first and then left.", "I did it first and then left.")]
    [InlineData("We should do the first thing, then more.", "We should do the first thing, then more.")]
    public void AnythingElse_IsLeftAlone(string input, string answer)
    {
        Assert.Equal(answer, ListLeadIn.Restore(input, answer));
    }
}
