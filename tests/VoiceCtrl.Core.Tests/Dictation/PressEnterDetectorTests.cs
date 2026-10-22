using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Press Enter (CONTEXT; spec §5.3): only a final "press enter" counts.</summary>
public class PressEnterDetectorTests
{
    [Theory]
    [InlineData("Sounds good, press enter", "Sounds good")]
    [InlineData("Sounds good. Press enter.", "Sounds good.")]
    [InlineData("See you at noon press Enter!", "See you at noon")]
    [InlineData("On my way. PRESS ENTER", "On my way.")]
    [InlineData("Shipped it! Press enter.  ", "Shipped it!")]
    [InlineData("First line\nSecond line\npress enter", "First line\nSecond line")]
    [InlineData("Thanks — press enter", "Thanks")]
    [InlineData("Thanks; press-enter.", "Thanks")]
    [InlineData("Is that right? Press   enter?", "Is that right?")]
    public void AFinalPressEnter_IsStripped(string spoken, string text)
    {
        PressEnterResult result = PressEnterDetector.Detect(spoken);

        Assert.True(result.PressEnter);
        Assert.Equal(text, result.Text);
    }

    [Theory]
    [InlineData("Press enter to continue.")]
    [InlineData("I told him to press enter and wait.")]
    [InlineData("Just compress enter")]
    [InlineData("Press enterprise")]
    [InlineData("Sounds good, enter")]
    [InlineData("")]
    public void AnythingElse_IsLeftAlone(string spoken)
    {
        PressEnterResult result = PressEnterDetector.Detect(spoken);

        Assert.False(result.PressEnter);
        Assert.Equal(spoken, result.Text);
    }

    [Fact]
    public void OnlyPressEnter_LeavesNothingToPaste()
    {
        PressEnterResult result = PressEnterDetector.Detect("Press enter.");

        Assert.True(result.PressEnter);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public void OnlyTheLastOne_IsStripped()
    {
        PressEnterResult result = PressEnterDetector.Detect("Type press enter, then press enter");

        Assert.Equal("Type press enter, then", result.Text);
    }
}
