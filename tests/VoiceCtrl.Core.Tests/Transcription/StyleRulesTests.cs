using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>Ticket 56: what each Style does to the finished text, the same for either engine.</summary>
public class StyleRulesTests
{
    private static readonly HashSet<string> Ordinary = new(StringComparer.Ordinal)
    {
        "yeah", "sounds", "hi", "thanks", "best", "the", "it", "we", "so", "can", "mark", "that",
    };

    private static string Styled(string text, Style style, string level = CleanupLevels.Standard) =>
        StyleRules.Apply(text, style, level, Ordinary.Contains);

    // The owner's approved preview (2026-10-19).
    [Fact]
    public void TheApprovedPreview_ReadsAsItWasShown()
    {
        Assert.Equal("Yes, sounds good. I will be there in five minutes.", Styled("Yeah, sounds good. I'll be there in five minutes.", Style.Formal));
        Assert.Equal("Yeah, sounds good, I'll be there in five minutes", Styled("Yeah, sounds good, I'll be there in five minutes.", Style.Casual));
    }

    [Theory]
    [InlineData("I don't think it's ready.", "I do not think it is ready.")]
    [InlineData("We won't make it, can't be helped.", "We will not make it, cannot be helped.")]
    [InlineData("They're sure we've got it.", "They are sure we have got it.")]
    [InlineData("I'm gonna call, I wanna know.", "I am going to call, I want to know.")]
    [InlineData("It's been a long week.", "It has been a long week.")]
    [InlineData("I'd like that. I'd better go.", "I would like that. I had better go.")]
    [InlineData("Sarah's report is late.", "Sarah's report is late.")]
    [InlineData("Let's meet at noon.", "Let's meet at noon.")]
    [InlineData("Yep. Nope.", "Yes. No.")]
    [InlineData("I’ll send it.", "I will send it.")]
    [InlineData("That’s Mark’s.", "That is Mark’s.")]
    [InlineData("Doesn't matter.", "Does not matter.")]
    public void Formal_WritesContractionsOutAndCasualWordsFormally(string text, string expected)
    {
        Assert.Equal(expected, Styled(text, Style.Formal));
    }

    [Fact]
    public void Formal_AtWordForWord_KeepsEveryWordAsSaid()
    {
        Assert.Equal("Yeah, I'll be there.", Styled("Yeah, I'll be there.", Style.Formal, CleanupLevels.Verbatim));
    }

    [Theory]
    [InlineData("Sounds good.\nSee you there.", "Sounds good\nSee you there")]
    [InlineData("Is that ok?", "Is that ok?")]
    [InlineData("Ask Dr.", "Ask Dr.")]
    public void Casual_DropsTheFullStopClosingEachLine(string text, string expected)
    {
        Assert.Equal(expected, Styled(text, Style.Casual));
    }

    [Theory]
    [InlineData("Yeah, sounds good.\nI'll be there in five minutes.", "yeah, sounds good\nI'll be there in five minutes")]
    [InlineData("Sarah's coming too.", "Sarah's coming too")]
    [InlineData("Thanks. Can you check? Mark said so.", "thanks. can you check? mark said so")]
    [InlineData("API keys are fine.", "API keys are fine")]
    [InlineData("1. The budget\n2. Sounds fine", "1. the budget\n2. sounds fine")]
    [InlineData("Hi Maria,\n\nThe deck is ready.\n\nBest,\nTom", "hi Maria,\n\nthe deck is ready\n\nbest,\nTom")]
    public void VeryCasual_StartsSentencesLowercaseButKeepsIAndNames(string text, string expected)
    {
        Assert.Equal(expected, Styled(text, Style.VeryCasual));
    }

    [Fact]
    public void VeryCasual_WithoutAWordList_KeepsEveryCapital()
    {
        Assert.Equal("Sounds good", StyleRules.Apply("Sounds good.", Style.VeryCasual, CleanupLevels.Standard));
    }

    [Theory]
    [InlineData("Yeah, sounds good. I'll be there in five minutes.", "Yeah, sounds good! I'll be there in five minutes!")]
    [InlineData("Is it ready? It is.", "Is it ready? It is!")]
    [InlineData("It costs 3.50 at 5 p.m.", "It costs 3.50 at 5 p.m.")]
    [InlineData("Wait... ok.", "Wait... ok!")]
    [InlineData("See example.com today.", "See example.com today!")]
    [InlineData("1. Book it.\n2. Pay.", "1. Book it!\n2. Pay!")]
    public void Excited_EndsSentencesWithExclamationMarks(string text, string expected)
    {
        Assert.Equal(expected, Styled(text, Style.Excited));
    }

    [Fact]
    public void TheStyle_ComesLast_AfterTheChatLayout()
    {
        PromptContext chat = new(CleanupLevels.Standard, null, null, null, EmailLayout: false, Chat: ChatLayout.Paragraphs, Kind: AppKind.WorkChat, Style: Style.Formal);

        Assert.Equal("Yes, sounds good. I will be there at five.", PasteLayout.Finish("Yeah, sounds good. I'll be there at five.", chat));
    }

    [Fact]
    public void AiPromptsAndCode_HaveNoStyle_SoTheirWordsStayAsSaid()
    {
        PromptContext context = GeminiPrompt.ContextFor(null, "cursor", new PromptDefaults(CleanupLevels.Standard, true, [], []));

        Assert.Equal("Don't rename the API.", PasteLayout.Finish("Don't rename the API.", context, Ordinary.Contains));
    }

    [Fact]
    public void AnAppMovedOnTheStyleScreen_TakesItsNewKindsStyle()
    {
        var styles = new StyleSettings { AppKinds = new Dictionary<string, AppKind> { ["Discord"] = AppKind.WorkChat }, WorkChat = Style.Formal };
        PromptContext context = GeminiPrompt.ContextFor(null, "discord", new PromptDefaults(CleanupLevels.Standard, true, [], [], Styles: styles));

        Assert.Equal(AppKind.WorkChat, context.Kind);
        Assert.Equal(Style.Formal, context.Style);
    }

    [Fact]
    public void AnUnlistedAppMovedByItsProcessName_TakesItsNewKind()
    {
        var styles = new StyleSettings { AppKinds = new Dictionary<string, AppKind> { ["mattermost"] = AppKind.WorkChat } };

        Assert.Equal(AppKind.WorkChat, AppKinds.Of("Mattermost", moved: styles.AppKinds));
    }
}
