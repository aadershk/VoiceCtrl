namespace VoiceCtrl.Core.Overlay;

/// <summary>A rectangle in the Overlay page's CSS pixels, as the page reports it.</summary>
public readonly record struct PageRect(double X, double Y, double Width, double Height);

/// <summary>
/// The Overlay window's geometry (spec §7, research 15): one fixed-size transparent window at the
/// bottom centre of the active monitor, with the pill drawn inside it by the page. The window
/// never resizes; the pill morphs within it. Also converts between the page's CSS pixels and the
/// mouse hook's physical screen pixels (CSS pixels are DIPs: WebView2 rasterizes at the
/// monitor's scale).
/// </summary>
public static class OverlayGeometry
{
    /// <summary>Room for the widest state (Failed, 196) plus its shadow on both sides.</summary>
    public const int WindowWidthDip = 320;

    /// <summary>The 32 px pill, the 10 px rise it appears from, and its shadow.</summary>
    public const int WindowHeightDip = 96;

    /// <summary>How far above the window's bottom edge the page draws the pill's bottom edge.
    /// Must match <c>--pill-bottom</c> in <c>ui/src/overlay/overlay.css</c>. The space below the
    /// pill holds its shadow, and reaches over the taskbar (the window is click-through).</summary>
    public const int PillBottomInsetDip = 32;

    /// <summary>The pill floats this far above the taskbar (DESIGN §9, Bottom dock).</summary>
    public const int GapAboveTaskbarDip = 12;

    /// <summary>The hit rectangle reaches this far past the pill on every side (spec §7).</summary>
    public const int HitMarginDip = 4;

    /// <summary>On the Left and Right docks the pill sits this far from the screen edge (the
    /// approved board). It is also the room the window leaves for the shadow on that side, so the
    /// window's edge is the work area's edge.</summary>
    public const int SideGapDip = 16;

    /// <summary>The pill's centre line in the window, from the top: the same on every dock.</summary>
    public const double PillCenterYDip = WindowHeightDip - PillBottomInsetDip - 16;

    /// <summary>Where the window goes on a monitor with this work area (rcWork, physical px) and
    /// effective DPI scale, for <paramref name="dock"/>.</summary>
    public static PixelRect Window(PixelRect workArea, double dpiScale, OverlayDock dock = OverlayDock.Bottom)
    {
        PixelRect bottom = OverlayPlacement.BottomCenter(
            workArea,
            dpiScale,
            WindowWidthDip,
            WindowHeightDip,
            bottomMarginDip: GapAboveTaskbarDip - PillBottomInsetDip);
        if (dock == OverlayDock.Bottom)
        {
            return bottom;
        }

        // Same size; centred vertically, flush with the edge (the gap is inside the window).
        int top = workArea.Top + ((workArea.Height - bottom.Height) / 2);
        int left = dock == OverlayDock.Left ? workArea.Left : workArea.Right - bottom.Width;
        return bottom with { Left = left, Top = top };
    }

    /// <summary>
    /// The pill's left edge in the window, in DIPs, for a pill <paramref name="widthDip"/> wide:
    /// Bottom grows from the centre, Left rightwards from its left edge, Right leftwards from its
    /// right edge (the page's <c>layout.ts</c> does the same sum).
    /// </summary>
    public static double PillLeftDip(OverlayDock dock, double widthDip) => dock switch
    {
        OverlayDock.Left => SideGapDip,
        OverlayDock.Right => WindowWidthDip - SideGapDip - widthDip,
        _ => (WindowWidthDip - widthDip) / 2,
    };

    /// <summary>
    /// The dock whose resting tile (32 × 32) is nearest to a screen point (physical px) on a
    /// monitor with this work area: where a dragged pill snaps on release.
    /// </summary>
    public static OverlayDock NearestDock(int x, int y, PixelRect workArea, double dpiScale)
    {
        const double tile = 32;
        OverlayDock nearest = OverlayDock.Bottom;
        double best = double.MaxValue;
        foreach (OverlayDock dock in Enum.GetValues<OverlayDock>())
        {
            PixelRect window = Window(workArea, dpiScale, dock);
            double centreX = window.Left + ((PillLeftDip(dock, tile) + (tile / 2)) * dpiScale);
            double centreY = window.Top + (PillCenterYDip * dpiScale);
            double distance = ((centreX - x) * (centreX - x)) + ((centreY - y) * (centreY - y));
            if (distance < best)
            {
                best = distance;
                nearest = dock;
            }
        }

        return nearest;
    }

    /// <summary>
    /// The window position that keeps a pill <paramref name="pillWidthDip"/> wide exactly where it
    /// is on screen when the page re-anchors it from <paramref name="from"/>'s rule to
    /// <paramref name="to"/>'s. The snap animates from here, so the dock change never jumps.
    /// </summary>
    public static PixelRect Rebase(PixelRect window, double dpiScale, OverlayDock from, OverlayDock to, double pillWidthDip)
    {
        double shift = (PillLeftDip(from, pillWidthDip) - PillLeftDip(to, pillWidthDip)) * dpiScale;
        return window with { Left = window.Left + (int)Math.Round(shift) };
    }

    /// <summary>
    /// The screen area (physical px) the mouse hook treats as the pill: the page's rectangle plus
    /// <see cref="HitMarginDip"/>, rounded outward and clipped to the window. Null when the page
    /// reports no pill, or numbers that aren't a rectangle (the page is a system boundary, and a
    /// bad rectangle here would swallow clicks meant for other apps).
    /// </summary>
    public static PixelRect? HitRect(PageRect pill, PixelRect window, double dpiScale)
    {
        if (!double.IsFinite(pill.X) || !double.IsFinite(pill.Y) || !double.IsFinite(pill.Width) || !double.IsFinite(pill.Height)
            || pill.Width <= 0 || pill.Height <= 0)
        {
            return null;
        }

        int left = window.Left + (int)Math.Floor((pill.X - HitMarginDip) * dpiScale);
        int top = window.Top + (int)Math.Floor((pill.Y - HitMarginDip) * dpiScale);
        int right = window.Left + (int)Math.Ceiling((pill.X + pill.Width + HitMarginDip) * dpiScale);
        int bottom = window.Top + (int)Math.Ceiling((pill.Y + pill.Height + HitMarginDip) * dpiScale);

        left = Math.Max(left, window.Left);
        top = Math.Max(top, window.Top);
        right = Math.Min(right, window.Right);
        bottom = Math.Min(bottom, window.Bottom);
        return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : null;
    }

    /// <summary>A physical screen point in the page's CSS pixels.</summary>
    public static (double X, double Y) ToPage(int screenX, int screenY, PixelRect window, double dpiScale) =>
        ((screenX - window.Left) / dpiScale, (screenY - window.Top) / dpiScale);
}
