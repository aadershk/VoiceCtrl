using VoiceCtrl.Core.Overlay;

namespace VoiceCtrl.Core.Tray;

/// <summary>The menu corner nearest the tray icon: the corner the flyout grows from.</summary>
public enum TrayCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

/// <summary>
/// Where the tray flyout goes (DESIGN §12, spec §7), in physical pixels: 10 px from the tray icon,
/// on the side of it that faces the middle of the screen, with the menu's nearest corner lined up
/// on the icon, and always inside the work area. The window is the menu plus transparent room
/// for its shadow, which the page draws.
/// </summary>
public static class TrayFlyoutPlacement
{
    /// <summary>The menu sits this far from the icon.</summary>
    public const int GapDip = 10;

    /// <summary>Room around the menu for <c>--pop-shadow</c> (0 12px 32px): it reaches 20 px up,
    /// 32 px sideways and 44 px down. Must match the padding in <c>ui/src/flyout/flyout.css</c>.</summary>
    public const int ShadowTopDip = 20;

    /// <inheritdoc cref="ShadowTopDip"/>
    public const int ShadowSideDip = 32;

    /// <inheritdoc cref="ShadowTopDip"/>
    public const int ShadowBottomDip = 44;

    /// <summary>The corner the menu grows from, for an icon on a monitor with this work area. The
    /// menu opens towards the middle of the screen, which also covers a taskbar on any edge and
    /// an icon in the hidden-icons popup.</summary>
    public static TrayCorner Corner(PixelRect icon, PixelRect workArea)
    {
        bool below = Middle(icon.Top, icon.Height) > Middle(workArea.Top, workArea.Height);
        bool right = Middle(icon.Left, icon.Width) > Middle(workArea.Left, workArea.Width);
        return (below, right) switch
        {
            (true, true) => TrayCorner.BottomRight,
            (true, false) => TrayCorner.BottomLeft,
            (false, true) => TrayCorner.TopRight,
            (false, false) => TrayCorner.TopLeft,
        };
    }

    /// <summary>The menu's rectangle for a menu of this size in DIPs, at the icon's monitor's
    /// DPI scale.</summary>
    public static PixelRect Menu(PixelRect icon, PixelRect workArea, double dpiScale, double widthDip, double heightDip)
    {
        int width = Scale(widthDip, dpiScale);
        int height = Scale(heightDip, dpiScale);
        int gap = Scale(GapDip, dpiScale);
        TrayCorner corner = Corner(icon, workArea);

        int left = corner is TrayCorner.BottomRight or TrayCorner.TopRight ? icon.Right - width : icon.Left;
        int top = corner is TrayCorner.BottomRight or TrayCorner.BottomLeft ? icon.Top - gap - height : icon.Bottom + gap;

        // Inside the work area; a menu taller than it keeps its top (and its first items) on screen.
        left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
        top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));
        return new PixelRect(left, top, width, height);
    }

    /// <summary>The window around <paramref name="menu"/>: the menu plus its shadow room.</summary>
    public static PixelRect Window(PixelRect menu, double dpiScale)
    {
        int top = Scale(ShadowTopDip, dpiScale);
        int side = Scale(ShadowSideDip, dpiScale);
        int bottom = Scale(ShadowBottomDip, dpiScale);
        return new PixelRect(menu.Left - side, menu.Top - top, menu.Width + (2 * side), menu.Height + top + bottom);
    }

    private static double Middle(int start, int length) => start + (length / 2.0);

    private static int Scale(double dip, double dpiScale) => (int)Math.Round(dip * dpiScale);
}
