using VoiceCtrl.Core.Hotkey;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class DoubleTapDetectorTests
{
    [Fact]
    public void SecondTapWithinWindow_ReturnsTrue()
    {
        var detector = new DoubleTapDetector(windowMs: 400);
        Assert.False(detector.RegisterTap(0));
        Assert.True(detector.RegisterTap(300));
    }

    [Fact]
    public void SecondTapOutsideWindow_ReturnsFalse()
    {
        var detector = new DoubleTapDetector(windowMs: 400);
        Assert.False(detector.RegisterTap(0));
        Assert.False(detector.RegisterTap(600));
    }

    [Fact]
    public void TapExactlyAtWindowBoundary_ReturnsTrue()
    {
        var detector = new DoubleTapDetector(windowMs: 400);
        Assert.False(detector.RegisterTap(0));
        Assert.True(detector.RegisterTap(400));
    }

    [Fact]
    public void ThirdTapAfterDoubleTap_StartsFreshWindowInsteadOfChaining()
    {
        var detector = new DoubleTapDetector(windowMs: 400);
        Assert.False(detector.RegisterTap(0));
        Assert.True(detector.RegisterTap(300));
        Assert.False(detector.RegisterTap(320));
        Assert.True(detector.RegisterTap(500));
    }

    [Fact]
    public void RapidFireFiveTaps_FiresExactlyOncePerQualifyingPair()
    {
        var detector = new DoubleTapDetector(windowMs: 400);
        bool[] results =
        [
            detector.RegisterTap(0),
            detector.RegisterTap(100),
            detector.RegisterTap(200),
            detector.RegisterTap(300),
            detector.RegisterTap(400),
        ];

        Assert.Equal([false, true, false, true, false], results);
    }
}
