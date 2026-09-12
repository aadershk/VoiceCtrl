namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Tracks the Trigger Key's Tap/Hold gesture: a single key, or a chord (a held prefix plus any
/// second key), per .scratch/hub-ui-refresh/issues/03-design-hotkey-editor.md and its Addendum.
/// Unlike the Hotkey's double-tap (<see cref="CtrlKeyTracker"/>, driven purely by keydown/keyup
/// timestamps), Tap vs. Hold can't be decided from key events alone: nothing happens while a key is
/// simply held, so telling "a quick tap" from "the start of a hold" needs a real elapsed-time
/// check with no further key event to trigger it. That check is <see cref="CheckHoldThreshold"/>,
/// left for the caller to drive from an actual timer (a <see cref="System.Windows.Threading.DispatcherTimer"/>
/// armed for the hold threshold once <see cref="IsEngaged"/> goes true) — this class stays a pure,
/// tick-driven state machine so the threshold logic itself is unit-testable without a real clock.
///
/// A chord "engages" only once both its prefix and second key are down together (order: prefix
/// first, matching how a person actually presses a chord); releasing either one ends it, per the
/// Addendum's "releasing either key stops it". A single-key Trigger Key only ever has the one key
/// to track, so <see cref="_secondVk"/> is null and engagement is just that key being down.
/// </summary>
public sealed class TriggerKeyTracker
{
    private readonly int _primaryVk;
    private readonly int? _secondVk;
    private readonly long _holdThresholdMs;

    private bool _primaryDown;
    private bool _secondDown;
    private bool _holdFired;
    private long _engagedAtTick;

    public event Action? Tapped;
    public event Action? HoldStarted;
    public event Action? HoldEnded;

    /// <param name="primaryVk">The key itself in single-key mode, or the chord's held prefix.</param>
    /// <param name="secondVk">Null in single-key mode; the chord's open-ended second key otherwise.</param>
    public TriggerKeyTracker(int primaryVk, int? secondVk, long holdThresholdMs)
    {
        _primaryVk = primaryVk;
        _secondVk = secondVk;
        _holdThresholdMs = holdThresholdMs;
    }

    /// <summary>True while every key this Trigger Key needs (the bare key, or both chord keys) is
    /// currently down. Read by the caller right after <see cref="OnKeyDown"/> to notice a fresh
    /// engagement and arm the hold-threshold timer.</summary>
    public bool IsEngaged { get; private set; }

    /// <summary>Call for every keydown while the Trigger Key is enabled (untracked VKs are cheap
    /// no-ops); OS key-repeat on an already-down key is naturally ignored since it can't flip
    /// <see cref="_primaryDown"/>/<see cref="_secondDown"/> from already-true to true again.</summary>
    public void OnKeyDown(int vk, long nowTicksMs)
    {
        if (vk == _primaryVk)
        {
            _primaryDown = true;
        }
        else if (_secondVk is int second && vk == second)
        {
            _secondDown = true;
        }
        else
        {
            return;
        }

        if (!IsEngaged && IsSatisfied())
        {
            IsEngaged = true;
            _holdFired = false;
            _engagedAtTick = nowTicksMs;
        }
    }

    /// <summary>Call once from a timer armed for the hold threshold when <see cref="IsEngaged"/>
    /// went true. Safe to call after the key was already released (engagement will have gone false
    /// by then, so this is a no-op) — callers may stop the timer on release for tidiness, but
    /// correctness does not depend on that happening before a stale tick lands.</summary>
    public void CheckHoldThreshold(long nowTicksMs)
    {
        if (IsEngaged && !_holdFired && nowTicksMs - _engagedAtTick >= _holdThresholdMs)
        {
            _holdFired = true;
            HoldStarted?.Invoke();
        }
    }

    /// <summary>Call for every keyup while the Trigger Key is enabled. Releasing either key of an
    /// engaged chord ends it, same as releasing a plain single key.</summary>
    public void OnKeyUp(int vk, long nowTicksMs)
    {
        if (vk == _primaryVk)
        {
            _primaryDown = false;
        }
        else if (_secondVk is int second && vk == second)
        {
            _secondDown = false;
        }
        else
        {
            return;
        }

        if (IsEngaged && !IsSatisfied())
        {
            IsEngaged = false;
            if (_holdFired)
            {
                _holdFired = false;
                HoldEnded?.Invoke();
            }
            else
            {
                Tapped?.Invoke();
            }
        }
    }

    private bool IsSatisfied() => _secondVk is null ? _primaryDown : _primaryDown && _secondDown;
}
