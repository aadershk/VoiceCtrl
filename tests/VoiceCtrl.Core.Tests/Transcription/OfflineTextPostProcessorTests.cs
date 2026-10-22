using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class OfflineTextPostProcessorTests
{
    [Fact]
    public void StripsStandaloneFillerWords()
    {
        string result = OfflineTextPostProcessor.Clean("um so I think uh we should go");
        Assert.Equal("So I think we should go.", result);
    }

    [Fact]
    public void CollapsesImmediatelyRepeatedWords()
    {
        string result = OfflineTextPostProcessor.Clean("send send the the file to Sarah");
        Assert.Equal("Send the file to Sarah.", result);
    }

    [Fact]
    public void CapitalizesFirstLetter()
    {
        string result = OfflineTextPostProcessor.Clean("hello there");
        Assert.Equal("Hello there.", result);
    }

    [Fact]
    public void AddsTerminalPunctuationWhenMissing()
    {
        string result = OfflineTextPostProcessor.Clean("this has no ending punctuation");
        Assert.EndsWith(".", result);
    }

    [Fact]
    public void DoesNotDuplicateExistingTerminalPunctuation()
    {
        string result = OfflineTextPostProcessor.Clean("is this a question?");
        Assert.Equal("Is this a question?", result);
    }

    [Fact]
    public void CollapsesInternalWhitespace()
    {
        string result = OfflineTextPostProcessor.Clean("too   many    spaces");
        Assert.Equal("Too many spaces.", result);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        string result = OfflineTextPostProcessor.Clean("   ");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void IsCaseInsensitiveForFillerWords()
    {
        string result = OfflineTextPostProcessor.Clean("Um, Uh, this works");
        Assert.DoesNotContain("Um", result);
        Assert.DoesNotContain("Uh", result);
    }

    // Seen in a real offline transcript: stripping the filler from between two commas left the
    // punctuation behind, so the text came back as ", , roll it out to staging first".
    [Fact]
    public void FillerBetweenCommas_DoesNotLeaveTheCommasBehind()
    {
        string result = OfflineTextPostProcessor.Clean("so, uh, roll it out to staging first");
        Assert.Equal("So, roll it out to staging first.", result);
    }

    // Ticket 55: the recogniser sets a filler off with commas, so the comma left behind can split a
    // phrase no comma belongs in ("Can you, do three things").
    [Theory]
    [InlineData("Can you, um, do three things for me.", "Can you do three things for me.")]
    [InlineData("Could we, uh, move the call.", "Could we move the call.")]
    [InlineData("I sent it to the, um, client.", "I sent it to the client.")]
    [InlineData("We should, uh, ship it.", "We should ship it.")]
    [InlineData("Hi Sarah, um, thanks for the deck.", "Hi Sarah, thanks for the deck.")]
    [InlineData("Thank you, um, see you then.", "Thank you, see you then.")]
    public void AFillersCommas_GoWhereNoCommaBelongs(string spoken, string expected)
    {
        Assert.Equal(expected, OfflineTextPostProcessor.Clean(spoken));
    }

    [Fact]
    public void UtteranceOpeningWithAFiller_DoesNotStartOnAComma()
    {
        string result = OfflineTextPostProcessor.Clean("um, roll it out");
        Assert.Equal("Roll it out.", result);
    }

    [Fact]
    public void SpokenNewParagraph_BecomesABlankLine()
    {
        string result = OfflineTextPostProcessor.Clean("first point new paragraph second point");
        Assert.Equal("First point\n\nSecond point.", result);
    }

    [Fact]
    public void SpokenNewLine_BecomesASingleBreak()
    {
        string result = OfflineTextPostProcessor.Clean("milk new line eggs");
        Assert.Equal("Milk\nEggs.", result);
    }

    // Wispr Flow's four line phrases (research 31, row 5), so a speaker who knows it can say any.
    [Theory]
    [InlineData("milk next line eggs", "Milk\nEggs.")]
    [InlineData("milk line break eggs", "Milk\nEggs.")]
    [InlineData("milk skip a line eggs", "Milk\n\nEggs.")]
    public void TheOtherLinePhrases_BreakTheLineToo(string spoken, string expected)
    {
        Assert.Equal(expected, OfflineTextPostProcessor.Clean(spoken));
    }

    [Theory]
    [InlineData("the next line of the file is wrong")]
    [InlineData("we should not skip a line of defence here")]
    [InlineData("a line break in the middle of a word is bad")]
    [InlineData("my next line needs a fix")]
    public void TheOtherLinePhrasesUsedAsOrdinaryEnglish_AreLeftAlone(string spoken)
    {
        Assert.DoesNotContain('\n', OfflineTextPostProcessor.Clean(spoken));
    }

    [Fact]
    public void CommaJoiningTwoClausesAcrossABreak_IsNotLeftDangling()
    {
        string result = OfflineTextPostProcessor.Clean("ship the fix, new paragraph, then tell the team");
        Assert.Equal("Ship the fix\n\nThen tell the team.", result);
    }

    // The reason these two commands can be on by default at all: without the guards, an ordinary
    // sentence that happens to contain the words would be torn in half.
    [Theory]
    [InlineData("we are entering a new line of business")]
    [InlineData("the new paragraph needs another read")]
    [InlineData("check my new line before sending it")]
    public void NewLineOrParagraphUsedAsOrdinaryEnglish_IsLeftAlone(string spoken)
    {
        string result = OfflineTextPostProcessor.Clean(spoken);
        Assert.DoesNotContain('\n', result);
    }

    [Fact]
    public void SpokenPunctuation_IsOffUnlessAskedFor()
    {
        string result = OfflineTextPostProcessor.Clean("the comma is missing here");
        Assert.Equal("The comma is missing here.", result);
    }

    [Fact]
    public void SpokenPunctuation_AttachesToThePrecedingWordWhenEnabled()
    {
        string result = OfflineTextPostProcessor.Clean(
            "wait comma we should ship period", applySpokenPunctuation: true);
        Assert.Equal("Wait, we should ship.", result);
    }

    [Theory]
    [InlineData("is it ready question mark", "Is it ready?")]
    [InlineData("that is great exclamation mark", "That is great!")]
    [InlineData("stop full stop", "Stop.")]
    public void SpokenPunctuation_CoversTheMultiWordForms(string spoken, string expected)
    {
        Assert.Equal(expected, OfflineTextPostProcessor.Clean(spoken, applySpokenPunctuation: true));
    }

    [Fact]
    public void SentenceAfterTerminalPunctuation_IsCapitalized()
    {
        string result = OfflineTextPostProcessor.Clean("that is done. now for the next one");
        Assert.Equal("That is done. Now for the next one.", result);
    }

    [Fact]
    public void EachLineStartsCapitalized()
    {
        string result = OfflineTextPostProcessor.Clean("one thing new line another thing");
        Assert.Equal("One thing\nAnother thing.", result);
    }

    [Fact]
    public void StandalonePronounI_IsCapitalized()
    {
        string result = OfflineTextPostProcessor.Clean("i think i can do it");
        Assert.Equal("I think I can do it.", result);
    }

    [Theory]
    [InlineData("the i in the word", "The I in the word.")]
    [InlineData("ship it, i.e. today", "Ship it, i.e. today.")]
    public void PronounRule_DoesNotReachIntoAbbreviationsOrWords(string spoken, string expected)
    {
        Assert.Equal(expected, OfflineTextPostProcessor.Clean(spoken));
    }

    [Theory]
    [InlineData("ship it, e.g. tomorrow", "Ship it, e.g. tomorrow.")]
    [InlineData("the demo is at 9 a.m. sharp", "The demo is at 9 a.m. sharp.")]
    [InlineData("bring the deck, etc. and the notes", "Bring the deck, etc. and the notes.")]
    [InlineData("it is python vs. rust again", "It is python vs. rust again.")]
    public void AbbreviationFullStop_DoesNotStartANewSentence(string spoken, string expected)
    {
        Assert.Equal(expected, OfflineTextPostProcessor.Clean(spoken));
    }

    [Fact]
    public void OnlyFillers_ReturnsEmptyRatherThanStrayPunctuation()
    {
        Assert.Equal(string.Empty, OfflineTextPostProcessor.Clean("um, uh, erm"));
    }
}
