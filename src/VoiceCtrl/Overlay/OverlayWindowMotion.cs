using System.Diagnostics;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Overlay;

/// <summary>
/// Moves the Overlay window (spec §7): placed with its size on each show, moved without resizing
/// during a drag, and slid to a dock on the Glide spring. The page never moves the pill across the
/// screen itself; the window does, so the pill's hit rectangle and the page stay one fixed-size
/// surface. UI thread only.
/// </summary>
internal sealed class OverlayWindowMotion : IDisposable
{
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly IntPtr _handle;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1 };
    private (PixelRect From, PixelRect To, long Start)? _slide;

    public OverlayWindowMotion(IntPtr handle)
    {
        _handle = handle;
        _timer.Tick += (_, _) => Step();
    }

    /// <summary>Where the window is now, physical px.</summary>
    public PixelRect Rect { get; private set; }

    public bool IsSliding => _slide is not null;

    /// <summary>Raised after every move, with <c>resized</c> true when the size changed too.</summary>
    public event Action<bool>? Moved;

    /// <summary>Raised once when a slide has landed.</summary>
    public event Action? Landed;

    /// <summary>Puts the window at <paramref name="rect"/> at once, size included, back on top of
    /// the taskbar (which is topmost too). Stops any slide.</summary>
    public void Place(PixelRect rect)
    {
        Stop();
        bool resized = rect.Width != Rect.Width || rect.Height != Rect.Height;
        Rect = rect;
        NativeMethods.SetWindowPos(_handle, HwndTopmost, rect.Left, rect.Top, rect.Width, rect.Height, Win32.SWP_NOACTIVATE);
        Moved?.Invoke(resized);
    }

    /// <summary>Moves the window's top-left to <paramref name="rect"/>'s; never resizes.</summary>
    public void MoveTo(PixelRect rect)
    {
        if (rect.Left == Rect.Left && rect.Top == Rect.Top)
        {
            return;
        }

        Rect = Rect with { Left = rect.Left, Top = rect.Top };
        NativeMethods.SetWindowPos(_handle, HwndTopmost, Rect.Left, Rect.Top, 0, 0, Win32.SWP_NOACTIVATE | Win32.SWP_NOSIZE);
        Moved?.Invoke(false);
    }

    /// <summary>
    /// Slides from <paramref name="from"/> to <paramref name="to"/> on the Glide spring (420 ms),
    /// or jumps there under reduced motion. Each step is timed from the clock rather than counted
    /// in ticks, so a late WinForms tick costs smoothness, never duration.
    /// </summary>
    public void Slide(PixelRect from, PixelRect to, bool reducedMotion)
    {
        Stop();
        MoveTo(from);
        if (reducedMotion || from == to)
        {
            MoveTo(to);
            Landed?.Invoke();
            return;
        }

        _slide = (from, to, Stopwatch.GetTimestamp());
        _timer.Start();
    }

    /// <summary>Ends a slide where it is; the caller moves the window on from there.</summary>
    public void Stop()
    {
        _timer.Stop();
        _slide = null;
    }

    private void Step()
    {
        if (_slide is not { } slide)
        {
            _timer.Stop();
            return;
        }

        double elapsedMs = (Stopwatch.GetTimestamp() - slide.Start) * 1000.0 / Stopwatch.Frequency;
        double progress = elapsedMs / GlideSpring.Duration.TotalMilliseconds;
        MoveTo(GlideSpring.Lerp(slide.From, slide.To, GlideSpring.At(progress)));
        if (progress >= 1)
        {
            Stop();
            Landed?.Invoke();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
