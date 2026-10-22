using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.EmailRequests;

/// <summary>The email around the model's body (ticket 57): code sets the greeting and the sign-off,
/// so the names in them are only ever the request's and the user's own.</summary>
public class EmailTextTests
{
    private static readonly EmailRequest ToMark = new("Write an email to Mark thanking him.", "Mark");

    [Theory]
    [InlineData(Style.Formal, "Dear Mark,\n\nThank you.\n\nKind regards,\nAlex Kim")]
    [InlineData(Style.Casual, "Hi Mark,\n\nThank you.\n\nBest,\nAlex Kim")]
    [InlineData(Style.VeryCasual, "Hey Mark,\n\nThank you.\n\nCheers,\nAlex Kim")]
    public void TheEmail_IsGreetingBodySignOffAndName_InTheStylesWords(Style style, string expected) =>
        Assert.Equal(expected, EmailText.Assemble("Thank you.", ToMark, style, "Alex Kim"));

    [Fact]
    public void NoYourName_SignsWithNoName() =>
        Assert.Equal("Dear Mark,\n\nThank you.\n\nKind regards,", EmailText.Assemble("Thank you.", ToMark, Style.Formal, "  "));

    [Theory]
    [InlineData(null, Style.Formal, "Hello,")]
    [InlineData(null, Style.Casual, "Hi,")]
    [InlineData("team", Style.Formal, "Dear team,")]
    [InlineData("everyone", Style.Formal, "Hello everyone,")]
    [InlineData("all", Style.Casual, "Hi all,")]
    public void TheGreeting_FollowsTheRecipient(string? recipient, Style style, string greeting) =>
        Assert.StartsWith(greeting + "\n", EmailText.Assemble("Body.", new EmailRequest("Write an email.", recipient), style, ""));

    [Fact]
    public void TheBody_LosesTheGreetingSignOffAndSubjectTheModelAddedAnyway() =>
        Assert.Equal(
            "Thank you for the book.\n\nIt made my week.",
            EmailText.Body("Subject: Thanks\nHi Paul,\n\nThank you for the book.\n\nIt made my week.\n\nBest regards,\n[Your name]"));

    [Fact]
    public void TheBody_LosesAThinkingBlockTagsAndAChattyLead() =>
        Assert.Equal("Wednesday is fine.", EmailText.Body("<think>hm</think>Sure, here is the body:\n<body>Wednesday is fine.</body>"));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("Hi Paul,\n\nBest,")]
    public void NothingLeft_IsNoBody(string? reply) =>
        Assert.Null(EmailText.Body(reply));

    [Fact]
    public void ThePrompt_PutsTheRequestLast_WithWhoItIsToAndTheRegister()
    {
        string prompt = EmailText.GeminiPrompt([], ToMark, Style.Casual);

        Assert.EndsWith("<request>\nWrite an email to Mark thanking him.\n</request>\n\nTo: Mark\nRegister: friendly and relaxed", prompt);
    }
}
