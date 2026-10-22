namespace VoiceCtrl.Core.Overlay;

/// <summary>
/// One press on the Overlay's pill, from the button going down to it coming up (research 11,
/// Interaction). It stays a press, and may still be a click, until the pointer has moved
/// <see cref="ThresholdDip"/> away; from then on it is a drag and the window follows the pointer,
/// moved but never resized (spec §7). Screen coordinates are physical pixels, as the mouse hook
/// reports them.
/// </summary>
public sealed class OverlayDrag
{
    /// <summary>Windows' own drag distance (SM_CXDRAG) at 100%.</summary>
    public const int ThresholdDip = 4;

    private readonly int _pressX;
    private readonly int _pressY;
    private readonly double _thresholdPx;

    public OverlayDrag(int pressX, int pressY, PixelRect window, double dpiScale)
    {
        _pressX = pressX;
        _pressY = pressY;
        _thresholdPx = ThresholdDip * dpiScale;
        StartWindow = window;
    }

    /// <summary>The window when the button went down: where Esc puts it back.</summary>
    public PixelRect StartWindow { get; }

    public bool IsDragging { get; private set; }

    /// <summary>The window's new place for a pointer at (<paramref name="x"/>, <paramref name="y"/>),
    /// or null while the press hasn't yet moved far enough to be a drag.</summary>
    public PixelRect? Move(int x, int y)
    {
        int dx = x - _pressX;
        int dy = y - _pressY;
        if (!IsDragging && Math.Max(Math.Abs(dx), Math.Abs(dy)) <= _thresholdPx)
        {
            return null;
        }

        IsDragging = true;
        return StartWindow with { Left = StartWindow.Left + dx, Top = StartWindow.Top + dy };
    }
}
