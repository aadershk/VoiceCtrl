using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>Ticket 55: each app kind's built-in treatment reaches the prompt, and a Profile still
/// overrides it.</summary>
public class AppKindContextTests
{
    private static readonly PromptDefaults Defaults = new(CleanupLevels.Standard, ToneAwareness: true, [], []);

    [Theory]
    [InlineData("olk", null)]
    [InlineData("outlook", null)]
    [InlineData("chrome", "Inbox (3) - alex.doe@gmail.com - Gmail - Google Chrome")]
    public void AnEmailApp_LaysTextOutAsAnEmail_WithoutAProfile(string processName, string? title)
    {
        PromptContext context = GeminiPrompt.ContextFor(null, processName, Defaults, title);

        Assert.Equal(AppKind.Email, context.Kind);
        Assert.True(context.EmailLayout);
    }

    [Fact]
    public void AProfile_CanTurnEmailLayoutOff()
    {
        Assert.False(GeminiPrompt.ContextFor(new AppProfile { EmailLayout = false }, "olk", Defaults).EmailLayout);
    }

    [Fact]
    public void AProfile_CanTurnEmailLayoutOnForAnyApp()
    {
        Assert.True(GeminiPrompt.ContextFor(new AppProfile { EmailLayout = true }, "notepad", Defaults).EmailLayout);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("slack")]
    [InlineData("winword")]
    public void OtherApps_HaveNoEmailLayout(string processName)
    {
        Assert.False(GeminiPrompt.ContextFor(null, processName, Defaults).EmailLayout);
    }

    [Fact]
    public void AChatSiteInABrowser_IsACasualChat()
    {
        PromptContext context = GeminiPrompt.ContextFor(null, "chrome", Defaults, "(2) WhatsApp - Google Chrome");

        Assert.Equal(AppKind.PersonalChat, context.Kind);
        Assert.Equal(ChatLayout.OneParagraph, context.Chat);
        Assert.Equal(Style.Casual, context.Style);
        Assert.Contains("casual", context.ToneHint);
    }

    [Fact]
    public void APersonalChatMessage_IsOneParagraph_AndAWorkOne_ParagraphsByTopic()
    {
        Assert.Contains("one paragraph", GeminiPrompt.ContextFor(null, "whatsapp", Defaults).FormattingHint);
        Assert.Contains("where the topic changes", GeminiPrompt.ContextFor(null, "slack", Defaults).FormattingHint);
        Assert.DoesNotContain("own line", GeminiPrompt.ContextFor(null, "whatsapp", Defaults).FormattingHint);
    }

    [Theory]
    [InlineData("winword", null)]
    [InlineData("chrome", "Q3 plan - Google Docs - Google Chrome")]
    public void ADocument_IsWrittenInParagraphs(string processName, string? title)
    {
        PromptContext context = GeminiPrompt.ContextFor(null, processName, Defaults, title);

        Assert.Equal(AppKind.Document, context.Kind);
        Assert.Contains("paragraph", context.FormattingHint);
        Assert.Contains("heading", context.FormattingHint);
    }

    [Fact]
    public void AnAiPromptSite_GetsListsAndKeepsTechnicalTerms()
    {
        PromptContext context = GeminiPrompt.ContextFor(null, "chrome", Defaults, "ChatGPT - Google Chrome");

        Assert.Equal(AppKind.AiPrompt, context.Kind);
        Assert.Contains("Markdown", context.FormattingHint);
        Assert.Contains("technical terms", context.ToneHint);
    }

    [Fact]
    public void AProfileAskingForParagraphs_GivesAnyAppTheDocumentFormatting()
    {
        PromptContext context = GeminiPrompt.ContextFor(new AppProfile { Formatting = AppProfile.Paragraphs }, "notepad", Defaults);

        Assert.Contains("paragraph", context.FormattingHint);
    }

    [Theory]
    [InlineData("winword", AppProfile.Paragraphs)]
    [InlineData("slack", AppProfile.Prose)]
    [InlineData("cursor", AppProfile.Structured)]
    [InlineData("olk", AppProfile.None)]
    [InlineData("notepad", AppProfile.None)]
    public void ANewProfile_StartsWithTheFormattingItsKindGets(string processName, string expected)
    {
        Assert.Equal(expected, FormattingHintMapper.BuiltIn(processName));
    }
}
