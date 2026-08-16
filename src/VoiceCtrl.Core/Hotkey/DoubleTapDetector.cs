namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Pure double-tap timing logic, independent of Win32. Caller is responsible for
/// feeding it one call per de-duplicated logical key-down (no OS key-repeat).
/// </summary>
public sealed class DoubleTapDetector
{
    private readonly long _windowMs;
    private long _lastTapTick = -1;

    public DoubleTapDetector(long windowMs = 400)
    {
        _windowMs = windowMs;
    }

    /// <summary>
    /// Registers a tap at <paramref name="nowTicksMs"/> (caller should pass
    /// <see cref="Environment.TickCount64"/>, never wall-clock time, which can jump on
    /// clock adjustments). Returns true if this tap completes a double-tap.
    /// A completed double-tap consumes both taps, so a third rapid tap starts a fresh pair
    /// rather than chaining into a triple-tap.
    /// </summary>
    public bool RegisterTap(long nowTicksMs)
    {
        if (_lastTapTick >= 0 && nowTicksMs - _lastTapTick <= _windowMs)
        {
            _lastTapTick = -1;
            return true;
        }

        _lastTapTick = nowTicksMs;
        return false;
    }
}
