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
}
