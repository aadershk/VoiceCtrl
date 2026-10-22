using VoiceCtrl.Core.Overlay;
using Xunit;

namespace VoiceCtrl.Core.Tests.Overlay;

/// <summary>The Overlay window's place on screen and the conversions between the page's CSS
/// pixels and the mouse hook's physical screen pixels.</summary>
public class OverlayGeometryTests
{
    [Fact]
    public void Window_At100Percent_PutsThePill12pxAboveTheTaskbar()
    {
        // 1920x1080 at 100% with a 48 px taskbar.
        var workArea = new PixelRect(0, 0, 1920, 1032);

        PixelRect window = OverlayGeometry.Window(workArea, dpiScale: 1.0);

        Assert.Equal(OverlayGeometry.WindowWidthDip, window.Width);
        Assert.Equal(OverlayGeometry.WindowHeightDip, window.Height);
        Assert.Equal((1920 - window.Width) / 2, window.Left);

        // The pill's bottom edge sits PillBottomInsetDip above the window's bottom.
        int pillBottom = window.Bottom - OverlayGeometry.PillBottomInsetDip;
        Assert.Equal(1032 - 12, pillBottom);
    }

    [Fact]
    public void Window_At150PercentOnASecondMonitor_ScalesByThatMonitorsDpi()
    {
        var workArea = new PixelRect(1920, 0, 2880, 1560);

        PixelRect window = OverlayGeometry.Window(workArea, dpiScale: 1.5);

        Assert.Equal(OverlayGeometry.WindowWidthDip * 1.5, window.Width);
        Assert.Equal(OverlayGeometry.WindowHeightDip * 1.5, window.Height);
        Assert.Equal(1920 + ((2880 - window.Width) / 2), window.Left);
        Assert.Equal(1560 - 18, window.Bottom - (int)(OverlayGeometry.PillBottomInsetDip * 1.5));
    }

    [Fact]
    public void HitRect_ScalesFromTheWindowOriginAndGrowsBy4pxOnEverySide()
    {
        var window = new PixelRect(1000, 800, 480, 144);
        var pill = new PageRect(X: 100, Y: 40, Width: 54, Height: 32);

        PixelRect? hit = OverlayGeometry.HitRect(pill, window, dpiScale: 1.5);

        // (100 - 4) * 1.5 = 144 → 1144; (40 - 4) * 1.5 = 54 → 854; (54 + 8) * 1.5 = 93; (32 + 8) * 1.5 = 60.
        Assert.Equal(new PixelRect(1144, 854, 93, 60), hit);
    }

    [Fact]
    public void HitRect_RoundsOutward_SoAFractionalEdgeIsNeverLost()
    {
        var window = new PixelRect(0, 0, 400, 120);
        var pill = new PageRect(X: 10.3, Y: 10.3, Width: 20.2, Height: 20.2);

        PixelRect? hit = OverlayGeometry.HitRect(pill, window, dpiScale: 1.25);

        // Left (10.3 - 4) * 1.25 = 7.875 → 7; right (30.5 + 4) * 1.25 = 43.125 → 44.
        Assert.Equal(new PixelRect(7, 7, 37, 37), hit);
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(54, 0)]
    [InlineData(-1, 32)]
    public void HitRect_OfAnEmptyPill_IsNone(double width, double height)
    {
        PixelRect? hit = OverlayGeometry.HitRect(new PageRect(10, 10, width, height), new PixelRect(0, 0, 320, 96), 1.0);

        Assert.Null(hit);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void HitRect_FromAMalformedPageRect_IsNone(double x)
    {
        // The page is a system boundary: nonsense numbers must not become a swallowed screen area.
        PixelRect? hit = OverlayGeometry.HitRect(new PageRect(x, 10, 54, 32), new PixelRect(0, 0, 320, 96), 1.0);

        Assert.Null(hit);
    }

    [Fact]
    public void HitRect_IsClippedToTheWindow()
    {
        var window = new PixelRect(100, 100, 320, 96);

        PixelRect? hit = OverlayGeometry.HitRect(new PageRect(-500, -500, 5000, 5000), window, 1.0);

        Assert.Equal(window, hit);
    }

    [Fact]
    public void ToPage_UndoesTheWindowOriginAndScale()
    {
        var window = new PixelRect(1000, 800, 480, 144);

        (double x, double y) = OverlayGeometry.ToPage(1150, 860, window, dpiScale: 1.5);

        Assert.Equal(100, x);
        Assert.Equal(40, y);
    }
}
