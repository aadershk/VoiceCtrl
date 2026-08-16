using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class ToneHintMapperTests
{
    [Fact]
    public void KnownCasualApp_ResolvesToCasualHint()
    {
        string? result = ToneHintMapper.Resolve("slack");
        Assert.Contains("casual", result);
    }

    [Fact]
    public void KnownFormalApp_ResolvesToFormalHint()
    {
        string? result = ToneHintMapper.Resolve("outlook");
        Assert.Contains("professional", result);
    }

    [Fact]
    public void KnownCodeEditor_ResolvesToCodeHint()
    {
        string? result = ToneHintMapper.Resolve("devenv");
        Assert.Contains("code comment", result);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        Assert.Equal(ToneHintMapper.Resolve("slack"), ToneHintMapper.Resolve("SLACK"));
    }

    [Fact]
    public void UnknownProcessName_ReturnsNull()
    {
        Assert.Null(ToneHintMapper.Resolve("some_random_app"));
    }

    [Fact]
    public void NullProcessName_ReturnsNull()
    {
        Assert.Null(ToneHintMapper.Resolve(null));
    }
}
