using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>Ticket 55: the layout every engine's text gets last, in code, so Online and Offline agree
/// and the speaker's words are never touched.</summary>
public class PasteLayoutTests
{
    private static readonly PromptContext Other = new("standard", null, null, null, EmailLayout: false);
    private static readonly PromptContext Email = Other with { EmailLayout = true, Kind = AppKind.Email };
    private static readonly PromptContext Chat = Other with { Chat = ChatLayout.OneParagraph, Kind = AppKind.PersonalChat, Style = Style.Casual };
    private static readonly PromptContext WorkChat = Other with { Chat = ChatLayout.Paragraphs, Kind = AppKind.WorkChat, Style = Style.Casual };

    /// <summary>The built-in word list's verdict, as the app passes it: names are not ordinary words.</summary>
    private static readonly Func<string, bool> Ordinary = word => word is not ("emma" or "sam" or "priya");
    private static readonly PromptContext Document = Other with { Kind = AppKind.Document, FormattingHint = FormattingHintMapper.For(AppKind.Document) };

    // ---- Every app

    [Fact]
    public void MarkdownHardBreaks_NeverReachThePastedText()
    {
        Assert.Equal("Hi Sarah,\nThanks for the deck.", PasteLayout.Finish("Hi Sarah,  \nThanks for the deck.  ", Other));
    }

    [Fact]
    public void MoreThanOneBlankLine_IsOneBlankLine()
    {
        Assert.Equal("One.\n\nTwo.", PasteLayout.Finish("One.\n\n\n\nTwo.", Other));
    }

    [Fact]
    public void AListsLeadIn_StartsItsOwnLine()
    {
        Assert.Equal(
            "Quick update. The build is done.\nBefore Friday, can you do two things:\n1. Update the page\n2. Send the report",
            PasteLayout.Finish("Quick update. The build is done. Before Friday, can you do two things:\n1. Update the page\n2. Send the report", Other));
    }

    [Fact]
    public void AShortLeadIn_StaysOnItsSentencesLine()
    {
        Assert.Equal(
            "I need you to refactor the user service. It should:\n- Use the repository pattern\n- Add tests",
            PasteLayout.Finish("I need you to refactor the user service. It should:\n- Use the repository pattern\n- Add tests", Other));
    }

    // ---- A list the speaker spelled out is a list, whichever engine ran and whatever it did

    [Theory]
    [InlineData(
        "Can you do three things for me. First update the pricing page, second fix the broken link, and third send the report to the client.",
        "Can you do three things for me:\n1. Update the pricing page\n2. Fix the broken link\n3. Send the report to the client")]
    [InlineData(
        "Hey Sam. Before Friday, can you do two things: first, update the pricing page, and second, send the report to the client. Thanks, Alex.",
        "Hey Sam.\nBefore Friday, can you do two things:\n1. Update the pricing page\n2. Send the report to the client\nThanks, Alex.")]
    [InlineData(
        "First open the file, then second check the log.",
        "1. Open the file\n2. Check the log")]
    [InlineData(
        "Can you do two things:\nfirst, update the pricing page\nsecond, send the report",
        "Can you do two things:\n1. Update the pricing page\n2. Send the report")]
    public void ASpokenList_IsANumberedList(string text, string expected)
    {
        Assert.Equal(expected, PasteLayout.Finish(text, Other));
    }

    [Theory]
    [InlineData("I did it first and then left.")]
    [InlineData("First come, first served.")]
    [InlineData("Revenue grew in the first and second quarter.")]
    [InlineData("First of all, the second draft is better.")]
    public void FirstAndSecondAsOrdinaryWords_AreLeftAlone(string text)
    {
        Assert.Equal(text, PasteLayout.Finish(text, Other));
    }

    // ---- An opening filler goes, except word for word

    [Theory]
    [InlineData("So, quick update on the launch.", "Quick update on the launch.")]
    [InlineData("Okay so I need you to refactor the user service.", "I need you to refactor the user service.")]
    [InlineData("Well, the build is done.", "The build is done.")]
    [InlineData("So far the build is fine.", "So far the build is fine.")]
    [InlineData("Okay.", "Okay.")]
    public void AnOpeningFiller_IsDropped(string text, string expected)
    {
        Assert.Equal(expected, PasteLayout.Finish(text, Other));
    }

    [Fact]
    public void WordForWord_KeepsAnOpeningFiller()
    {
        Assert.Equal("So, quick update on the launch.", PasteLayout.Finish("So, quick update on the launch.", Other with { CleanupLevel = "verbatim" }));
    }

    // ---- Email

    [Fact]
    public void AnEmailBodyInOneLineASentence_IsOneParagraph()
    {
        Assert.Equal(
            "Quick update on the launch. The build is done.\n\nI'm a bit worried about the timeline.",
            PasteLayout.Finish("Quick update on the launch.\nThe build is done.\n\nI'm a bit worried about the timeline.", Email));
    }

    [Fact]
    public void AnEmail_GetsItsGreetingAndSignOffOnTheirOwnLines()
    {
        Assert.Equal(
            "Hi Sarah,\n\nThanks for sending the deck over. Can we go through it tomorrow morning?\n\nThanks,\nAlex",
            PasteLayout.Finish("Hi Sarah, thanks for sending the deck over. Can we go through it tomorrow morning? Thanks, Alex.", Email));
    }

    [Theory]
    [InlineData("Dear Mr Lee, thank you for your time today.", "Dear Mr Lee,\n\nThank you for your time today.")]
    [InlineData("Hello team, the release is out.", "Hello team,\n\nThe release is out.")]
    [InlineData("Good morning Priya, the invoice is attached.", "Good morning Priya,\n\nThe invoice is attached.")]
    [InlineData("Hey Sam. The build is done.", "Hey Sam,\n\nThe build is done.")]
    public void EveryCommonGreeting_GetsItsOwnLine(string text, string expected)
    {
        Assert.Equal(expected, PasteLayout.Finish(text, Email));
    }

    [Theory]
    [InlineData("Let me know. Best regards, Priya.", "Let me know.\n\nBest regards,\nPriya")]
    [InlineData("Let me know. Kind regards, Tom Baker", "Let me know.\n\nKind regards,\nTom Baker")]
    [InlineData("Let me know. Cheers, Jo!", "Let me know.\n\nCheers,\nJo")]
    [InlineData("Let me know. Thanks!", "Let me know.\n\nThanks!")]
    [InlineData("Let me know. Thank you.", "Let me know.\n\nThank you.")]
    public void EveryCommonSignOff_GetsItsOwnLine(string text, string expected)
    {
        Assert.Equal(expected, PasteLayout.Finish(text, Email));
    }

    [Theory]
    [InlineData("Thank you for your time today.")]
    [InlineData("Hello world is the first program I wrote.")]
    [InlineData("Yeah, sounds good. I will be there in five minutes.")]
    [InlineData("Best of luck with the launch.")]
    public void AnEmailWithNoGreetingOrSignOff_IsLeftAsItIs(string text)
    {
        Assert.Equal(text, PasteLayout.Finish(text, Email));
    }

    [Fact]
    public void AnEmailAlreadyLaidOut_IsLeftAsItIs()
    {
        const string laidOut = "Hey Sam,\n\nQuick update on the launch.\n\nBefore Friday, can you do two things:\n1. Update the pricing page\n2. Send the report\n\nThanks,\nAlex";

        Assert.Equal(laidOut, PasteLayout.Finish(laidOut, Email));
    }

    [Fact]
    public void AListInAnEmail_IsSetOffByBlankLines()
    {
        Assert.Equal(
            "Hey Sam,\n\nThe build is done.\n\nBefore Friday, can you do two things:\n1. Update the page\n2. Send the report\n\nThanks,\nAlex",
            PasteLayout.Finish("Hey Sam, the build is done. Before Friday, can you do two things:\n1. Update the page\n2. Send the report\nThanks, Alex.", Email));
    }

    // ---- Chat

    // Ticket 59 (owner, 2026-10-20): a personal chat message is one paragraph, like a typed text; a
    // work chat message is short paragraphs by topic. Casual still drops the closing full stop.

    [Theory]
    [InlineData("Sounds good, see you at five.", "Sounds good, see you at five")]
    [InlineData("Sounds good. See you at five.", "Sounds good. See you at five")]
    [InlineData("Haha yeah that was so funny.\nAre you coming tonight or not?", "Haha yeah that was so funny. Are you coming tonight or not?")]
    [InlineData("First the flights.\n\nThen the hotel.", "First the flights. Then the hotel")]
    [InlineData("Wait...", "Wait...")]
    [InlineData("Ask about the plan etc.", "Ask about the plan etc.")]
    [InlineData("It costs 3.50.", "It costs 3.50")]
    public void APersonalChatMessage_IsOneParagraph(string text, string expected)
    {
        Assert.Equal(expected, PasteLayout.Finish(text, Chat, Ordinary));
    }

    [Theory]
    [InlineData("Hey Emma\nThe hotel's booked.\nWe land Friday at 10pm.", "Hey Emma, the hotel's booked. We land Friday at 10pm")]
    [InlineData("Hey Emma,\nThe hotel's booked.", "Hey Emma, the hotel's booked")]
    [InlineData("Hi Sam\nEmma says hi.", "Hi Sam, Emma says hi")]
    [InlineData("Hey\nI booked it.", "Hey, I booked it")]
    [InlineData("All done\nSee you soon.", "All done. See you soon")]
    public void ALineTheModelBrokeOff_JoinsTheParagraph(string text, string expected)
    {
        Assert.Equal(expected, PasteLayout.Finish(text, Chat, Ordinary));
    }

    [Theory]
    [InlineData("The build is done.\nTesting starts Monday.\n\nAlso, the offsite moved to May.", "The build is done. Testing starts Monday\n\nAlso, the offsite moved to May")]
    [InlineData("I've pushed the fix. Can you take a look when you're free?", "I've pushed the fix. Can you take a look when you're free?")]
    public void AWorkChatMessage_IsShortParagraphs(string text, string expected)
    {
        Assert.Equal(expected, PasteLayout.Finish(text, WorkChat, Ordinary));
    }

    [Fact]
    public void AChatList_KeepsItsLines()
    {
        Assert.Equal(
            "Can you do two things:\n1. Update the page\n2. Send the report",
            PasteLayout.Finish("Can you do two things:\n1. Update the page.\n2. Send the report.", Chat));
        Assert.Equal(
            "The build is done. Before Friday, can you do two things:\n1. Update the page\n2. Send the report",
            PasteLayout.Finish("The build is done.\nBefore Friday, can you do two things:\n1. Update the page.\n2. Send the report.", Chat));
    }

    // ---- Documents

    [Fact]
    public void AListInADocument_IsSetOffByBlankLines()
    {
        Assert.Equal(
            "The build is done.\n\nCan you do two things:\n1. Update the page\n2. Send the report\n\nThanks, Alex.",
            PasteLayout.Finish("The build is done. Can you do two things:\n1. Update the page\n2. Send the report\nThanks, Alex.", Document));
    }
}
