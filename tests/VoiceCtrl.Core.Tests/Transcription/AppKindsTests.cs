using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>Ticket 55: which kind of app a Dictation lands in, from the process and, for a browser,
/// the site in the window title.</summary>
public class AppKindsTests
{
    [Theory]
    [InlineData("olk", AppKind.Email)]
    [InlineData("OUTLOOK", AppKind.Email)]
    [InlineData("thunderbird", AppKind.Email)]
    [InlineData("slack", AppKind.WorkChat)]
    [InlineData("WhatsApp", AppKind.PersonalChat)]
    [InlineData("ms-teams", AppKind.WorkChat)]
    [InlineData("Discord", AppKind.PersonalChat)]
    [InlineData("WINWORD", AppKind.Document)]
    [InlineData("Notion", AppKind.Document)]
    [InlineData("ONENOTE", AppKind.Document)]
    [InlineData("cursor", AppKind.AiPrompt)]
    [InlineData("Code", AppKind.AiPrompt)]
    [InlineData("WindowsTerminal", AppKind.AiPrompt)]
    [InlineData("claude", AppKind.AiPrompt)]
    [InlineData("ChatGPT", AppKind.AiPrompt)]
    [InlineData("notepad", AppKind.Other)]
    [InlineData("some_random_app", AppKind.Other)]
    [InlineData(null, AppKind.Other)]
    public void AnApp_IsKnownByItsProcess(string? processName, AppKind expected)
    {
        Assert.Equal(expected, AppKinds.Of(processName));
    }

    [Theory]
    [InlineData("chrome", "Inbox (3) - alex.doe@gmail.com - Gmail - Google Chrome", AppKind.Email)]
    [InlineData("msedge", "Mail - Alex Doe - Outlook and 2 more pages - Personal - Microsoft​ Edge", AppKind.Email)]
    [InlineData("chrome", "(2) WhatsApp - Google Chrome", AppKind.PersonalChat)]
    [InlineData("msedge", "launch (Channel) - Acme - Slack - Microsoft​ Edge", AppKind.WorkChat)]
    [InlineData("chrome", "Chat | Sarah Lee | Microsoft Teams - Google Chrome", AppKind.WorkChat)]
    [InlineData("firefox", "#general | Friends - Discord — Mozilla Firefox", AppKind.PersonalChat)]
    [InlineData("chrome", "Messenger | Facebook - Google Chrome", AppKind.PersonalChat)]
    [InlineData("chrome", "Telegram Web - Google Chrome", AppKind.PersonalChat)]
    [InlineData("chrome", "Q3 plan - Google Docs - Google Chrome", AppKind.Document)]
    [InlineData("chrome", "ChatGPT - Google Chrome", AppKind.AiPrompt)]
    [InlineData("msedge", "Planning the sprint - Claude - Microsoft​ Edge", AppKind.AiPrompt)]
    [InlineData("brave", "Google Gemini - Brave", AppKind.AiPrompt)]
    [InlineData("chrome", "Amazon.com: Online Shopping - Google Chrome", AppKind.Other)]
    [InlineData("chrome", null, AppKind.Other)]
    public void ABrowser_IsKnownByTheSiteInItsTitle(string processName, string? title, AppKind expected)
    {
        Assert.Equal(expected, AppKinds.Of(processName, title));
    }

    [Theory]
    [InlineData("Re: Slack invite - alex.doe@gmail.com - Gmail - Google Chrome", AppKind.Email)]
    [InlineData("My Outlook for Q3 - Google Docs - Google Chrome", AppKind.Document)]
    [InlineData("Why I left Slack - Medium - Google Chrome", AppKind.Other)]
    public void APageTitleNamingAnotherSite_DoesNotDecideTheKind(string title, AppKind expected)
    {
        // The site's own name is the last part of the title before the browser's; a subject line or
        // page title that only mentions a site is not that site.
        Assert.Equal(expected, AppKinds.Of("chrome", title));
    }

    [Fact]
    public void AWindowTitle_IsIgnoredOutsideABrowser()
    {
        Assert.Equal(AppKind.Other, AppKinds.Of("notepad", "Gmail notes.txt - Notepad"));
    }

    [Fact]
    public void TheAdvancedSettingsLists_WinOverTheBuiltInKind()
    {
        Assert.Equal(AppKind.WorkChat, AppKinds.Of("windowsterminal", null, extraAiPromptApps: [], extraChatApps: ["windowsterminal"]));
        Assert.Equal(AppKind.AiPrompt, AppKinds.Of("notepad", null, extraAiPromptApps: ["notepad"], extraChatApps: []));
    }
}
