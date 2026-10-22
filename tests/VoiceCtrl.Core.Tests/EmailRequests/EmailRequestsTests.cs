using VoiceCtrl.Core.EmailRequests;

namespace VoiceCtrl.Core.Tests.EmailRequests;

/// <summary>A Rewrite key instruction said with nothing selected (ticket 57): a request for a new email,
/// or an edit of the last Dictation. Both are commands, so anything unclear stays an edit, which is
/// what the Rewrite key did before.</summary>
public class EmailRequestsTests
{
    [Theory]
    [InlineData("Write an email to Mark thanking him for his time.", "Mark")]
    [InlineData("Can you draft a reply to James saying I'm happy to join the panel?", "James")]
    [InlineData("Okay, write an email to Priya congratulating her on the promotion.", "Priya")]
    [InlineData("Please write a short email to Dr. Patel confirming my appointment on the 14th.", "Dr. Patel")]
    [InlineData("Compose an email to Anna and Tom introducing them to each other.", "Anna and Tom")]
    [InlineData("Send Lisa an email saying the invoice is attached.", "Lisa")]
    [InlineData("Reply to Ben saying yes, Tuesday works for me.", "Ben")]
    [InlineData("Respond to Omar and tell him I can't make it tonight.", "Omar")]
    [InlineData("Email David and ask him whether he received my last message.", "David")]
    [InlineData("I would like you to draft an email to Ines requesting leave in December.", "Ines")]
    [InlineData("Please write Naomi a thank-you for the gift.", "Naomi")]
    [InlineData("Reply to Dan. Say yes to the proposal but ask for a week longer.", "Dan")]
    [InlineData("Write to Oscar and ask if he's free for lunch.", "Oscar")]
    [InlineData("Answer Dave's question about the budget. Tell him we have forty thousand left.", "Dave")]
    [InlineData("Something to Nadia telling her the report is attached.", "Nadia")]
    [InlineData("Follow up with Greg about the contract.", "Greg")]
    [InlineData("Tell Priya I'll be ten minutes late.", "Priya")]
    [InlineData("Let Jenny and Tom know that the dinner is at eight.", "Jenny and Tom")]
    [InlineData("I want to tell Kevin he did a great job on the presentation.", "Kevin")]
    [InlineData("Hey, quick message to Oliver, just say congrats on the new job.", "Oliver")]
    [InlineData("Write a reply to Sarah's question about pricing.", "Sarah")]
    public void ARequest_IsRecognised_WithTheRecipientItNames(string said, string recipient) =>
        Assert.Equal(recipient, EmailRequest.FromInstruction(said)?.Recipient);

    [Theory]
    [InlineData("Draft an email to the team letting them know the office is closed.", "team")]
    [InlineData("Write a thank-you email to everyone who came to the workshop.", "everyone")]
    [InlineData("Put together an email to the board about the results.", "all")]
    [InlineData("Write a message to the whole department saying the lift is fixed.", "all")]
    [InlineData("Put together an email to the marketing team with an update on the campaign.", "team")]
    [InlineData("Write an email to Sam, no wait, to Alex, saying the review moved.", "Alex")]
    [InlineData("Send Aisha a reminder about the dentist on Thursday, or no, make that Wednesday.", "Aisha")]
    [InlineData("Write an email to Marcus and also, actually, make that Marcus and Elena, inviting them.", "Marcus and Elena")]
    public void TheRecipient_IsTheGroupOrTheOneTheSpeakerSettledOn(string said, string recipient) =>
        Assert.Equal(recipient, EmailRequest.FromInstruction(said)?.Recipient);

    [Theory]
    [InlineData("Write a polite email declining the invitation to speak at the conference.")]
    [InlineData("Could you write an email to my landlord asking when the heating will be fixed?")]
    [InlineData("Respond to this email politely saying we can't offer a discount.")]
    [InlineData("Send my landlord a note that the rent will be three days late.")]
    [InlineData("I need to send a follow-up to the client about the proposal.")]
    [InlineData("Can you whip up a complaint to the hotel? The room was not cleaned.")]
    [InlineData("Reply saying sounds good, see you then.")]
    [InlineData("A thank-you note to the neighbours for the flowers would be great.")]
    public void ARequestNamingNoOne_HasNoRecipient(string said)
    {
        EmailRequest? request = EmailRequest.FromInstruction(said);

        Assert.NotNull(request);
        Assert.Null(request.Recipient);
    }

    [Fact]
    public void LinesFromTheTranscript_AreReadAsOneRequest() =>
        Assert.NotNull(EmailRequest.FromInstruction("Write an email to Sarah\nthanking her for the flowers."));

    [Theory]
    [InlineData("make this shorter")]
    [InlineData("shorten it")]
    [InlineData("turn this into bullet points")]
    [InlineData("turn this into a polite email")]
    [InlineData("make it more formal")]
    [InlineData("fix the grammar")]
    [InlineData("translate to Dutch")]
    [InlineData("say this in Spanish")]
    [InlineData("cut this down")]
    [InlineData("list the points")]
    [InlineData("correct the mistakes")]
    [InlineData("shorter please")]
    [InlineData("write this more formally")]
    [InlineData("um")]
    [InlineData("")]
    public void AnEditOrAnythingUnclear_IsNotARequest(string said) =>
        Assert.Null(EmailRequest.FromInstruction(said));
}
