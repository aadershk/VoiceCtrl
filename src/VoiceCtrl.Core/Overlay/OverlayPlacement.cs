namespace VoiceCtrl.Core.Overlay;

/// <summary>
/// Pure placement math for the Overlay bar. Everything here is in the destination monitor's
/// terms: the work area comes in as physical pixels and the bar's design size comes in as WPF
/// DIPs, and the result is the physical-pixel rectangle the window should occupy on that monitor.
/// Working in physical pixels is what makes the destination's DPI the one that counts, instead of
/// whatever DPI context the long-lived Overlay window happens to carry from its last showing.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>
    /// Bottom-center of <paramref name="workArea"/>, with the bar's bottom edge
    /// <paramref name="bottomMarginDip"/> above the work-area bottom (0 = flush with it).
    /// </summary>
    /// <param name="workArea">The monitor's work area (rcWork: excludes the taskbar), in physical pixels.</param>
    /// <param name="dpiScale">The monitor's effective DPI scale (1.0 = 96 DPI, 1.5 = 144 DPI).</param>
    /// <param name="barWidthDip">The bar window's designed width, in DIPs.</param>
    /// <param name="barHeightDip">The bar window's designed height, in DIPs.</param>
    /// <param name="bottomMarginDip">Gap between the bar's bottom edge and the work-area bottom, in DIPs.</param>
    public static PixelRect BottomCenter(
        PixelRect workArea,
        double dpiScale,
        double barWidthDip,
        double barHeightDip,
        double bottomMarginDip)
    {
        int widthPx = ToPixels(barWidthDip, dpiScale);
        int heightPx = ToPixels(barHeightDip, dpiScale);
        int marginPx = ToPixels(bottomMarginDip, dpiScale);

        // Integer division on the horizontal slack: when the slack is odd the bar sits half a
        // pixel left of true center, which is invisible and keeps the result an exact pixel.
        int left = workArea.Left + ((workArea.Width - widthPx) / 2);
        int top = workArea.Bottom - marginPx - heightPx;

        return new PixelRect(left, top, widthPx, heightPx);
    }

    private static int ToPixels(double dip, double dpiScale) =>
        (int)Math.Round(dip * dpiScale, MidpointRounding.AwayFromZero);
}
