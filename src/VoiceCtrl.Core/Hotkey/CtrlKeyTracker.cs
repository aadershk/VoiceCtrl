namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Tracks double-taps for one or more Ctrl keys independently, guarding against ordinary
/// modifier use (Ctrl+C, Ctrl+V, ...): a press only counts as a qualifying tap if no other key
/// went down while it was held. Firing therefore happens on release, once a hold is confirmed
/// clean, since a chord can only be ruled out after the fact, never at the moment a key goes down.
/// As a side effect this also rejects AltGr on non-US layouts, which Windows synthesizes as
/// VK_LCONTROL down + VK_RMENU down.
/// </summary>
public sealed class CtrlKeyTracker
{
    private readonly Dictionary<int, TrackedKeyState> _trackedKeys;

    public event Action? DoubleTapDetected;

    public CtrlKeyTracker(long doubleTapWindowMs, IEnumerable<int> trackedVkCodes)
    {
        _trackedKeys = trackedVkCodes.ToDictionary(
            vk => vk,
            _ => new TrackedKeyState(new DoubleTapDetector(doubleTapWindowMs)));
    }

    /// <summary>Call for every non-injected keydown (pass the resolved VK, or null if untracked).</summary>
    public void OnKeyDown(int? trackedVk, long nowTicksMs)
    {
        if (trackedVk is int vk && _trackedKeys.TryGetValue(vk, out TrackedKeyState? key))
        {
            if (!key.IsDown)
            {
                key.IsDown = true;
                key.WasChorded = false;
                key.DownTick = nowTicksMs;
            }
            return;
        }

        // Some other key went down while a tracked Ctrl is held, so that Ctrl is being used as a
        // modifier, so poison it: its eventual release must not count as a bare tap. Deliberately
        // does NOT cross-poison Left/Right against each other when only the two tracked keys are
        // involved, and each owns an independent detector, so holding both is not itself a chord.
        foreach (TrackedKeyState other in _trackedKeys.Values)
        {
            if (other.IsDown)
            {
                other.WasChorded = true;
            }
        }
    }

    /// <summary>Call for every non-injected keyup (untracked ones are ignored).</summary>
    public void OnKeyUp(int? trackedVk)
    {
        if (trackedVk is not int vk || !_trackedKeys.TryGetValue(vk, out TrackedKeyState? key) || !key.IsDown)
        {
            return;
        }

        key.IsDown = false;

        // Anchor window math at the DOWN tick, not "now", which keeps a long un-chorded hold anchored
        // at when it started, matching the original single-key design, instead of at release time.
        if (!key.WasChorded && key.Detector.RegisterTap(key.DownTick))
        {
            DoubleTapDetected?.Invoke();
        }
    }

    private sealed class TrackedKeyState(DoubleTapDetector detector)
    {
        public DoubleTapDetector Detector { get; } = detector;
        public bool IsDown { get; set; }
        public bool WasChorded { get; set; }
        public long DownTick { get; set; }
    }
}
