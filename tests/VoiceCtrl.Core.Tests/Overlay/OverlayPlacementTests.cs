using VoiceCtrl.Core.Overlay;
using Xunit;

namespace VoiceCtrl.Core.Tests.Overlay;

public class OverlayPlacementTests
{
    [Fact]
    public void BottomCenter_At100Percent_SitsFlushOnWorkAreaBottomEdge()
    {
        // A 1920x1080 primary monitor at 100% with a 48px taskbar: work area is 1920x1032.
        var workArea = new PixelRect(Left: 0, Top: 0, Width: 1920, Height: 1032);

        PixelRect bar = OverlayPlacement.BottomCenter(workArea, dpiScale: 1.0, barWidthDip: 220, barHeightDip: 130, bottomMarginDip: 0);

        Assert.Equal(new PixelRect(Left: 850, Top: 902, Width: 220, Height: 130), bar);
    }

    [Fact]
    public void BottomCenter_OnSecondaryMonitorAt150Percent_ScalesBarAndMarginByThatMonitorsDpi()
    {
        // A 2880x1620 external monitor at 150% sitting to the right of a 1920-wide primary, with a
        // 60px taskbar. Same 220x130 DIP bar as at 100%, but here it must occupy 330x195 pixels.
        var workArea = new PixelRect(Left: 1920, Top: 0, Width: 2880, Height: 1560);

        PixelRect bar = OverlayPlacement.BottomCenter(workArea, dpiScale: 1.5, barWidthDip: 220, barHeightDip: 130, bottomMarginDip: 24);

        // Left: 1920 + (2880 - 330) / 2. Top: 1560 - 36px margin - 195px height.
        Assert.Equal(new PixelRect(Left: 3195, Top: 1329, Width: 330, Height: 195), bar);
    }

    [Fact]
    public void BottomCenter_At125Percent_RoundsHalfPixelsAwayFromZero()
    {
        var workArea = new PixelRect(Left: 0, Top: 0, Width: 1600, Height: 900);

        PixelRect bar = OverlayPlacement.BottomCenter(workArea, dpiScale: 1.25, barWidthDip: 220, barHeightDip: 130, bottomMarginDip: 0);

        // 130 * 1.25 = 162.5 rounds up to 163, and the bar still ends exactly on the work-area bottom.
        Assert.Equal(163, bar.Height);
        Assert.Equal(275, bar.Width);
        Assert.Equal(900, bar.Bottom);
    }
}
