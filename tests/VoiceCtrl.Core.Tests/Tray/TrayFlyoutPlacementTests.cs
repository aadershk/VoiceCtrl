using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Core.Tray;
using Xunit;

namespace VoiceCtrl.Core.Tests.Tray;

/// <summary>Where the tray flyout goes (DESIGN §12): 10 px from the icon, growing from the menu
/// corner nearest it, inside the work area, with room around the menu for its shadow.</summary>
public class TrayFlyoutPlacementTests
{
    // 1920x1080 at 100% with a 48 px taskbar at the bottom; the icon sits near the clock.
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1032);
    private static readonly PixelRect IconNearClock = new(1760, 1040, 24, 32);

    [Fact]
    public void Corner_ForAnIconAtTheBottomRight_IsBottomRight()
    {
        Assert.Equal(TrayCorner.BottomRight, TrayFlyoutPlacement.Corner(IconNearClock, WorkArea));
    }

    [Fact]
    public void Corner_ForATopTaskbarOnTheLeft_IsTopLeft()
    {
        var workArea = new PixelRect(0, 48, 1920, 1032);
        var icon = new PixelRect(200, 8, 24, 32);

        Assert.Equal(TrayCorner.TopLeft, TrayFlyoutPlacement.Corner(icon, workArea));
    }

    [Fact]
    public void Menu_AboveABottomTaskbarIcon_Sits10pxAboveItWithItsRightEdgeOnTheIcons()
    {
        PixelRect menu = TrayFlyoutPlacement.Menu(IconNearClock, WorkArea, dpiScale: 1.0, widthDip: 292, heightDip: 300);

        Assert.Equal(292, menu.Width);
        Assert.Equal(300, menu.Height);
        Assert.Equal(IconNearClock.Top - 10, menu.Bottom);
        Assert.Equal(IconNearClock.Right, menu.Right);
    }

    [Fact]
    public void Menu_BelowATopTaskbarIcon_Sits10pxBelowItWithItsLeftEdgeOnTheIcons()
    {
        var workArea = new PixelRect(0, 48, 1920, 1032);
        var icon = new PixelRect(200, 8, 24, 32);

        PixelRect menu = TrayFlyoutPlacement.Menu(icon, workArea, dpiScale: 1.0, widthDip: 292, heightDip: 300);

        Assert.Equal(icon.Bottom + 10, menu.Top);
        Assert.Equal(icon.Left, menu.Left);
    }

    [Fact]
    public void Menu_At150Percent_ScalesTheSizeAndTheGap()
    {
        var workArea = new PixelRect(0, 0, 2880, 1548);
        var icon = new PixelRect(2640, 1560, 36, 48);

        PixelRect menu = TrayFlyoutPlacement.Menu(icon, workArea, dpiScale: 1.5, widthDip: 292, heightDip: 300);

        Assert.Equal(438, menu.Width);
        Assert.Equal(450, menu.Height);
        Assert.Equal(icon.Top - 15, menu.Bottom);
    }

    [Fact]
    public void Menu_NeverLeavesTheWorkArea()
    {
        // An icon hard against the right edge of a right-hand taskbar, low down.
        var workArea = new PixelRect(0, 0, 1872, 1080);
        var icon = new PixelRect(1884, 1000, 24, 24);

        PixelRect menu = TrayFlyoutPlacement.Menu(icon, workArea, dpiScale: 1.0, widthDip: 292, heightDip: 300);

        Assert.True(menu.Right <= workArea.Right);
        Assert.True(menu.Left >= workArea.Left);
        Assert.True(menu.Top >= workArea.Top);
        Assert.True(menu.Bottom <= workArea.Bottom);
    }

    [Fact]
    public void Menu_TallerThanTheWorkArea_KeepsItsTopOnScreen()
    {
        var workArea = new PixelRect(0, 0, 800, 200);
        var icon = new PixelRect(760, 210, 24, 32);

        PixelRect menu = TrayFlyoutPlacement.Menu(icon, workArea, dpiScale: 1.0, widthDip: 292, heightDip: 300);

        Assert.Equal(workArea.Top, menu.Top);
    }

    [Fact]
    public void Window_IsTheMenuWithRoomForItsShadow()
    {
        var menu = new PixelRect(1000, 500, 292, 300);

        PixelRect window = TrayFlyoutPlacement.Window(menu, dpiScale: 1.0);

        Assert.Equal(menu.Left - TrayFlyoutPlacement.ShadowSideDip, window.Left);
        Assert.Equal(menu.Top - TrayFlyoutPlacement.ShadowTopDip, window.Top);
        Assert.Equal(menu.Right + TrayFlyoutPlacement.ShadowSideDip, window.Right);
        Assert.Equal(menu.Bottom + TrayFlyoutPlacement.ShadowBottomDip, window.Bottom);
    }

    [Fact]
    public void Window_At150Percent_ScalesTheShadowRoom()
    {
        var menu = new PixelRect(1000, 500, 438, 450);

        PixelRect window = TrayFlyoutPlacement.Window(menu, dpiScale: 1.5);

        Assert.Equal(menu.Left - (int)Math.Round(TrayFlyoutPlacement.ShadowSideDip * 1.5), window.Left);
        Assert.Equal(menu.Bottom + (int)Math.Round(TrayFlyoutPlacement.ShadowBottomDip * 1.5), window.Bottom);
    }
}
