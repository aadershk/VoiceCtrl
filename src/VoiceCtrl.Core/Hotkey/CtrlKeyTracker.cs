namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Tracks double-taps for one or more Ctrl keys independently, guarding against ordinary
/// modifier use (Ctrl+C, Ctrl+V, ...): a press only counts as a qualifying tap if no other key
/// went down while it was held. Firing therefore happens on release, once a hold is confirmed
/// clean, since a chord can only be ruled out after the fact, never at the moment a key goes down.
/// As a side effect this also rejects AltGr on non-US layouts, which Windows synthesizes as
/// VK_LCONTROL down + VK_RMENU down.
///
/// Each key's <see cref="TrackedKeyState.IsDown"/> flag is set on <see cref="OnKeyDown"/> and
/// cleared on <see cref="OnKeyUp"/> with nothing else to catch it going stale: Windows can
/// silently fail to deliver a key-up (most reliably reproduced by opening the Start Menu and
/// dismissing it by clicking elsewhere, rather than pressing Win or Esc again), which leaves a
/// flag stuck "down" after the physical key has actually been released. Left unchecked, a later
/// genuine press of that same key would then be misread as OS auto-repeat of the still-held key
/// and silently no-op'd, corrupting or swallowing that key's double-tap.
///
/// Unlike <see cref="TriggerKeyTracker"/>'s chord, a single tracked key has no OTHER key of its
/// own to cross-check when its own flag might be stale — and a real (non-injected) Windows
/// keydown for a key always means it is physically down *right now*, whether this is a fresh
/// press or OS auto-repeat of a continuous hold, so a key's own keydown can never tell those two
/// apart. The optional <see cref="_isKeyActuallyDown"/> collaborator instead reconciles every
/// OTHER tracked key's flag against real Windows key state on every keydown — the same shape as
/// <see cref="TriggerKeyTracker"/>'s cross-check, generalized to fire on any key event rather than
/// just a chord's second key. This catches a stale flag the next time any other tracked key (the
/// shipped default tracks Left and Right Ctrl together) or any unrelated key genuinely goes down,
/// without this class calling into Windows itself.
///
/// Known residual gap, inherent to the platform rather than fixable here: a Hotkey assigned to a
/// single key (see <see cref="HotkeyKeyCatalog"/>) whose user does nothing but repeatedly
/// double-tap that one key, with zero other keystrokes ever interleaved, has no "other" event to
/// reconcile against and so won't self-correct on the very first post-glitch attempt. Recovery
/// still happens the moment any other key — including the session's next ordinary keystroke —
/// is pressed.
/// </summary>
public sealed class CtrlKeyTracker
{
    private readonly Dictionary<int, TrackedKeyState> _trackedKeys;
    private readonly Func<int, bool>? _isKeyActuallyDown;

    public event Action? DoubleTapDetected;

    /// <param name="doubleTapWindowMs">Max gap between two taps of the same key to pair as a double-tap.</param>
    /// <param name="trackedVkCodes">The VK codes to track double-taps for, each independently.</param>
    /// <param name="isKeyActuallyDown">Reports whether a VK is genuinely down right now, per real
    /// Windows key state (e.g. backed by <c>GetAsyncKeyState</c> in <see cref="LowLevelKeyboardHook"/>).
    /// Null in tests that feed a complete, gap-free synthetic event stream and so have no stale
    /// state to cross-check.</param>
    public CtrlKeyTracker(long doubleTapWindowMs, IEnumerable<int> trackedVkCodes, Func<int, bool>? isKeyActuallyDown = null)
    {
        _trackedKeys = trackedVkCodes.ToDictionary(
            vk => vk,
            _ => new TrackedKeyState(new DoubleTapDetector(doubleTapWindowMs)));
        _isKeyActuallyDown = isKeyActuallyDown;
    }

    /// <summary>Call for every non-injected keydown (pass the resolved VK, or null if untracked).</summary>
    public void OnKeyDown(int? trackedVk, long nowTicksMs)
    {
        ReconcileStaleFlags(trackedVk);

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

    /// <summary>Cross-checks every tracked key OTHER than <paramref name="justPressedVk"/> — that
    /// one needs no checking, since receiving this very event proves it's really down — against
    /// real Windows key state, correcting any flag a missed key-up left stuck "down" before it can
    /// falsely poison a chord check or keep swallowing that key's own double-tap. A no-op when no
    /// <see cref="_isKeyActuallyDown"/> collaborator was supplied.</summary>
    private void ReconcileStaleFlags(int? justPressedVk)
    {
        if (_isKeyActuallyDown is null)
        {
            return;
        }

        foreach (KeyValuePair<int, TrackedKeyState> entry in _trackedKeys)
        {
            if (entry.Key != justPressedVk && entry.Value.IsDown && !_isKeyActuallyDown(entry.Key))
            {
                entry.Value.IsDown = false;
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
