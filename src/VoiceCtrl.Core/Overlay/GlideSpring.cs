namespace VoiceCtrl.Core.Overlay;

/// <summary>
/// The Glide spring (DESIGN §10, <c>--ease-spring</c>) for motion the host draws itself: the
/// Overlay window's snap to a dock, which moves the window rather than anything in the page. The
/// same critically damped curve (ω = 9.2 over the whole duration) that the CSS <c>linear()</c>
/// samples, so the window lands the way the page's own motion does.
/// </summary>
public static class GlideSpring
{
    /// <summary>--dur-shape: size and position changes.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(420);

    private const double Omega = 9.2;

    /// <summary>The eased value at <paramref name="progress"/> (0..1 of the duration). Exactly 1
    /// at and after the end, where the analytic curve is still a hair short.</summary>
    public static double At(double progress)
    {
        if (progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        double u = Omega * progress;
        return 1 - ((1 + u) * Math.Exp(-u));
    }

    /// <summary>The window between <paramref name="start"/> and <paramref name="end"/> at eased
    /// value <paramref name="eased"/>, moved but never resized.</summary>
    public static PixelRect Lerp(PixelRect start, PixelRect end, double eased) => start with
    {
        Left = (int)Math.Round(start.Left + ((end.Left - start.Left) * eased)),
        Top = (int)Math.Round(start.Top + ((end.Top - start.Top) * eased)),
    };
}
