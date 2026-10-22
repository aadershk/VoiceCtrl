namespace VoiceCtrl.Core.Overlay;

/// <summary>
/// A screen rectangle in physical pixels, as Win32 reports and consumes them. Kept free of WPF
/// types so the placement math can live in Core and be unit-tested without a window or monitor.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}
