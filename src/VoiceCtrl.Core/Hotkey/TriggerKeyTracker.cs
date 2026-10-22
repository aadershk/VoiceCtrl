namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Tracks the Trigger Key's Tap/Hold gesture: a single key, or a chord (a held prefix plus any
/// second key), per .scratch/hub-ui-refresh/issues/03-design-hotkey-editor.md and its Addendum.
/// Unlike the Hotkey's double-tap (<see cref="CtrlKeyTracker"/>, driven purely by keydown/keyup
/// timestamps), Tap vs. Hold can't be decided from key events alone: nothing happens while a key is
/// simply held, so telling "a quick tap" from "the start of a hold" needs a real elapsed-time
/// check with no further key event to trigger it (unless the threshold is 0: the hold then begins
/// at the key press and a release only ever ends a hold). That check is <see cref="CheckHoldThreshold"/>,
/// left for the caller to drive from an actual timer (a <see cref="System.Windows.Threading.DispatcherTimer"/>
/// armed for the hold threshold once <see cref="IsEngaged"/> goes true) — this class stays a pure,
/// tick-driven state machine so the threshold logic itself is unit-testable without a real clock.
///
/// A chord "engages" only once both its prefix and second key are down together (order: prefix
/// first, matching how a person actually presses a chord); releasing either one ends it, per the
/// Addendum's "releasing either key stops it". A single-key Trigger Key only ever has the one key
/// to track, so <see cref="_secondVk"/> is null and engagement is just that key being down.
///
/// Each key's "is it down" flag is set on <see cref="OnKeyDown"/> and cleared on
/// <see cref="OnKeyUp"/> with nothing else to catch it going stale: Windows can silently fail to
/// deliver a key-up (most reliably reproduced by opening the Start Menu and dismissing it by
/// clicking elsewhere, rather than pressing Win or Esc again), which leaves a flag stuck "down"
/// after the physical key has actually been released. Left unchecked, an unrelated later press of
/// the chord's other key would then falsely satisfy <see cref="IsSatisfied"/>. The optional
/// <see cref="_isKeyActuallyDown"/> collaborator lets the caller supply real Windows key state to
/// cross-check against right before that would happen, without this class calling into Windows
/// itself — it stays a pure, Win32-free state machine, same as before.
/// </summary>
public sealed class TriggerKeyTracker
{
    private readonly int[] _vks;
    private readonly long _holdThresholdMs;
    private readonly Func<int, bool>? _isKeyActuallyDown;
    private readonly bool _otherKeySpoilsHold;
    private readonly HashSet<int> _down = [];

    private bool _holdFired;
    private bool _spoiled;
    private long _engagedAtTick;

    public event Action? Tapped;
    public event Action? HoldStarted;
    public event Action? HoldEnded;

    /// <summary>Only with <c>otherKeySpoilsHold</c>: another key went down while the hold was
    /// running, so it was a shortcut after all. Raised once; no <see cref="HoldEnded"/> follows.</summary>
    public event Action? HoldCancelled;

    /// <param name="primaryVk">The key itself in single-key mode, or the chord's held prefix.</param>
    /// <param name="secondVk">Null in single-key mode; the chord's open-ended second key otherwise.</param>
    /// <param name="isKeyActuallyDown">Reports whether a VK is genuinely down right now, per real
    /// Windows key state (e.g. backed by <c>GetAsyncKeyState</c> in <see cref="LowLevelKeyboardHook"/>).
    /// Null in tests that feed a complete, gap-free synthetic event stream and so have no stale
    /// state to cross-check.</param>
    public TriggerKeyTracker(int primaryVk, int? secondVk, long holdThresholdMs, Func<int, bool>? isKeyActuallyDown = null)
        : this(secondVk is int second ? [primaryVk, second] : [primaryVk], holdThresholdMs, isKeyActuallyDown)
    {
    }

    /// <param name="vks">One to three keys that must all be down together, in any order.</param>
    /// <param name="otherKeySpoilsHold">True for a combination that is also the start of ordinary
    /// shortcuts (Ctrl + Shift + T): any other key pressed before the hold begins makes it a
    /// shortcut, so neither a hold nor a tap is reported.</param>
    public TriggerKeyTracker(IReadOnlyList<int> vks, long holdThresholdMs, Func<int, bool>? isKeyActuallyDown = null, bool otherKeySpoilsHold = false)
    {
        _vks = [.. vks.Distinct()];
        _holdThresholdMs = holdThresholdMs;
        _isKeyActuallyDown = isKeyActuallyDown;
        _otherKeySpoilsHold = otherKeySpoilsHold;
    }

    /// <summary>True while every key this Trigger Key needs (the bare key, or both chord keys) is
    /// currently down. Read by the caller right after <see cref="OnKeyDown"/> to notice a fresh
    /// engagement and arm the hold-threshold timer.</summary>
    public bool IsEngaged { get; private set; }

    /// <summary>True when two or more keys must be held together.</summary>
    public bool IsMultiKey => _vks.Length > 1;

    /// <summary>Call for every keydown while the Trigger Key is enabled (untracked VKs are cheap
    /// no-ops); OS key-repeat on an already-down key is naturally ignored since it can't flip
    /// <see cref="_primaryDown"/>/<see cref="_secondDown"/> from already-true to true again.</summary>
    public void OnKeyDown(int vk, long nowTicksMs)
    {
        if (!_vks.Contains(vk))
        {
            if (_otherKeySpoilsHold && IsEngaged && !_spoiled)
            {
                _spoiled = true;
                if (_holdFired)
                {
                    // The recording is already under way: say so, so it is thrown away.
                    _holdFired = false;
                    HoldCancelled?.Invoke();
                }
            }

            return;
        }

        _down.Add(vk);
        if (!IsEngaged && IsSatisfied())
        {
            ReconcileStaleFlags(vk);

            if (IsSatisfied())
            {
                IsEngaged = true;
                _holdFired = false;
                _spoiled = false;
                _engagedAtTick = nowTicksMs;

                // No hold delay (Hold-to-Talk records from the key press): the hold begins now,
                // there is no timer to wait for and so no tap to tell apart.
                CheckHoldThreshold(nowTicksMs);
            }
        }
    }

    /// <summary>Cross-checks whichever tracked key(s) did NOT just genuinely go down — the flag for
    /// the key named by <paramref name="justPressedVk"/> needs no checking, since receiving this
    /// very event proves it's really down — against real Windows key state, correcting any flag a
    /// missed key-up left stuck "down" before it can complete a false engagement. A no-op when no
    /// <see cref="_isKeyActuallyDown"/> collaborator was supplied.</summary>
    private void ReconcileStaleFlags(int justPressedVk)
    {
        if (_isKeyActuallyDown is null)
        {
            return;
        }

        foreach (int vk in _vks)
        {
            if (vk != justPressedVk && _down.Contains(vk) && !_isKeyActuallyDown(vk))
            {
                _down.Remove(vk);
            }
        }
    }

    /// <summary>Call once from a timer armed for the hold threshold when <see cref="IsEngaged"/>
    /// went true. Safe to call after the key was already released (engagement will have gone false
    /// by then, so this is a no-op) — callers may stop the timer on release for tidiness, but
    /// correctness does not depend on that happening before a stale tick lands.</summary>
    public void CheckHoldThreshold(long nowTicksMs)
    {
        if (IsEngaged && !_holdFired && !_spoiled && nowTicksMs - _engagedAtTick >= _holdThresholdMs)
        {
            _holdFired = true;
            HoldStarted?.Invoke();
        }
    }

    /// <summary>Call for every keyup while the Trigger Key is enabled. Releasing either key of an
    /// engaged chord ends it, same as releasing a plain single key.</summary>
    public void OnKeyUp(int vk, long nowTicksMs)
    {
        if (!_vks.Contains(vk))
        {
            return;
        }

        _down.Remove(vk);
        if (IsEngaged && !IsSatisfied())
        {
            IsEngaged = false;
            if (_holdFired)
            {
                _holdFired = false;
                HoldEnded?.Invoke();
            }
            else if (!_spoiled)
            {
                Tapped?.Invoke();
            }

            _spoiled = false;
        }
    }

    private bool IsSatisfied() => _vks.All(_down.Contains);
}
