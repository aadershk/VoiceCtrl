using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class CleanupLevelMapperTests
{
    [Fact]
    public void Verbatim_KeepsEveryWord_InsteadOfFallingBackToStandard()
    {
        string result = CleanupLevelMapper.Resolve("verbatim");

        Assert.Contains("word for word", result);
        Assert.NotEqual(CleanupLevelMapper.Resolve("standard"), result);
    }

    [Theory]
    [InlineData("verbatim", true)]
    [InlineData("light", true)]
    [InlineData("standard", true)]
    [InlineData("aggressive", true)]
    [InlineData("Standard", false)]
    [InlineData("thorough", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheFourStoredNames_AreValidLevels(string? level, bool valid) =>
        Assert.Equal(valid, CleanupLevels.IsValid(level));

    [Fact]
    public void Light_ResolvesToLightInstruction()
    {
        string result = CleanupLevelMapper.Resolve("light");
        Assert.Contains("Do not rephrase or restructure", result);
    }

    [Fact]
    public void Standard_ResolvesToStandardInstruction()
    {
        string result = CleanupLevelMapper.Resolve("standard");
        Assert.Contains("fix grammar, punctuation, and capitalization", result);
    }

    [Fact]
    public void Aggressive_ResolvesToAggressiveInstruction()
    {
        string result = CleanupLevelMapper.Resolve("aggressive");
        Assert.Contains("restructure sentences", result);
    }

    [Fact]
    public void UnknownLevel_FallsBackToStandard()
    {
        string result = CleanupLevelMapper.Resolve("nonsense");
        Assert.Equal(CleanupLevelMapper.Resolve("standard"), result);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        string result = CleanupLevelMapper.Resolve("AGGRESSIVE");
        Assert.Equal(CleanupLevelMapper.Resolve("aggressive"), result);
    }
}
