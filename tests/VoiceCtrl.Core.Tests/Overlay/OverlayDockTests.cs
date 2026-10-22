using VoiceCtrl.Core.Overlay;
using Xunit;

namespace VoiceCtrl.Core.Tests.Overlay;

/// <summary>The Bottom, Left and Right docks (research 11, Interaction; DESIGN §9): where each puts
/// the window, which way the pill grows, the nearest dock to a drop, and the snap's spring.</summary>
public class OverlayDockTests
{
    // 1920x1080 at 100% with a 48 px taskbar.
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1032);

    [Fact]
    public void Bottom_IsTheDefault_AndCentresThePill12pxAboveTheTaskbar()
    {
        Assert.Equal(OverlayGeometry.Window(WorkArea, 1.0), OverlayGeometry.Window(WorkArea, 1.0, OverlayDock.Bottom));
        Assert.Equal(160, OverlayGeometry.PillLeftDip(OverlayDock.Bottom, 0));
        Assert.Equal(160 - 27, OverlayGeometry.PillLeftDip(OverlayDock.Bottom, 54));
    }

    [Fact]
    public void Left_PutsThePill16pxFromTheLeftEdge_CentredVertically_GrowingRightwards()
    {
        PixelRect window = OverlayGeometry.Window(WorkArea, 1.0, OverlayDock.Left);

        Assert.Equal(0, window.Left);
        Assert.Equal((1032 - 96) / 2, window.Top);
        Assert.Equal(16, window.Left + OverlayGeometry.PillLeftDip(OverlayDock.Left, 32));
        Assert.Equal(16, window.Left + OverlayGeometry.PillLeftDip(OverlayDock.Left, 196));
    }

    [Fact]
    public void Right_PutsThePill16pxFromTheRightEdge_GrowingLeftwards()
    {
        PixelRect window = OverlayGeometry.Window(WorkArea, 1.0, OverlayDock.Right);

        Assert.Equal(1920, window.Right);
        Assert.Equal(1920 - 16, window.Left + OverlayGeometry.PillLeftDip(OverlayDock.Right, 54) + 54);
        Assert.Equal(1920 - 16, window.Left + OverlayGeometry.PillLeftDip(OverlayDock.Right, 196) + 196);
    }

    [Fact]
    public void SideDocks_ScaleByTheMonitorsDpi_OnASecondMonitor()
    {
        var workArea = new PixelRect(1920, 100, 2880, 1560);

        PixelRect left = OverlayGeometry.Window(workArea, 1.5, OverlayDock.Left);
        PixelRect right = OverlayGeometry.Window(workArea, 1.5, OverlayDock.Right);

        Assert.Equal(new PixelRect(1920, 100 + ((1560 - 144) / 2), 480, 144), left);
        Assert.Equal(new PixelRect(1920 + 2880 - 480, left.Top, 480, 144), right);
    }

    [Fact]
    public void EveryDock_KeepsTheWidestStateInsideTheWindow()
    {
        // Failed at 196 grows a little for "Service error"; 250 leaves room for that and the shadow.
        foreach (OverlayDock dock in Enum.GetValues<OverlayDock>())
        {
            double left = OverlayGeometry.PillLeftDip(dock, 250);
            Assert.InRange(left, 12, OverlayGeometry.WindowWidthDip - 250 - 12);
        }
    }

    [Theory]
    [InlineData(960, 1000, OverlayDock.Bottom)]
    [InlineData(700, 950, OverlayDock.Bottom)]
    [InlineData(60, 500, OverlayDock.Left)]
    [InlineData(300, 300, OverlayDock.Left)]
    [InlineData(1880, 516, OverlayDock.Right)]
    [InlineData(1500, 200, OverlayDock.Right)]
    public void NearestDock_IsTheOneWhosePillIsClosestToTheDrop(int x, int y, OverlayDock expected)
    {
        Assert.Equal(expected, OverlayGeometry.NearestDock(x, y, WorkArea, 1.0));
    }

    [Fact]
    public void Rebase_KeepsThePillWhereItIsOnScreen_WhenItsAnchorMovesToTheNewDock()
    {
        // Dragged from Bottom to near the left edge: the page will now anchor the pill by its left
        // edge rather than its centre, so the window shifts by the difference and nothing jumps.
        var window = new PixelRect(100, 400, 480, 144);

        PixelRect rebased = OverlayGeometry.Rebase(window, 1.5, OverlayDock.Bottom, OverlayDock.Left, pillWidthDip: 54);

        double before = window.Left + (OverlayGeometry.PillLeftDip(OverlayDock.Bottom, 54) * 1.5);
        double after = rebased.Left + (OverlayGeometry.PillLeftDip(OverlayDock.Left, 54) * 1.5);
        Assert.Equal(before, after, precision: 0);
        Assert.Equal(window.Top, rebased.Top);
        Assert.Equal(window.Width, rebased.Width);
    }

    [Fact]
    public void Spring_StartsAtRest_FollowsTheGlideCurve_AndLandsExactly()
    {
        Assert.Equal(0, GlideSpring.At(0));
        Assert.Equal(0.9437, GlideSpring.At(0.5), precision: 3);
        Assert.Equal(1, GlideSpring.At(1));
        Assert.Equal(1, GlideSpring.At(1.2));
    }

    [Fact]
    public void Lerp_MovesTheWindowWithoutResizingIt()
    {
        var from = new PixelRect(0, 0, 320, 96);
        var to = new PixelRect(1000, 500, 320, 96);

        Assert.Equal(new PixelRect(500, 250, 320, 96), GlideSpring.Lerp(from, to, 0.5));
        Assert.Equal(to, GlideSpring.Lerp(from, to, 1));
    }
}
