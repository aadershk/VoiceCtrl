using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class CtrlKeyTrackerTests
{
    private static CtrlKeyTracker CreateTracker(long windowMs = 400) =>
        new(windowMs, [NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL]);

    [Fact]
    public void BareDoubleTap_WithinWindow_Fires()
    {
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 300);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SecondTapOutsideWindow_DoesNotFire()
    {
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 600);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void TapExactlyAtWindowBoundary_Fires()
    {
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 400);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void LeftThenRight_DoesNotPair()
    {
        // Left and Right each own an independent DoubleTapDetector, so alternating between them
        // is not a double-tap of either.
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        tracker.OnKeyDown(NativeMethods.VK_RCONTROL, 50);
        tracker.OnKeyUp(NativeMethods.VK_RCONTROL);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void ChordedCtrl_DoesNotCountAsTap()
    {
        // The core scenario this redesign exists for: Ctrl held as a modifier (e.g. Ctrl+C)
        // must never register as a bare tap, no matter how quickly it's released.
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);
        tracker.OnKeyDown(null, 10); // 'C' goes down while Ctrl is held.
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void RapidCtrlCThenCtrlV_NeitherTapCounts()
    {
        // The exact false-positive this whole redesign exists to prevent: a fast copy-paste
        // must never pop the dictation overlay.
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);
        tracker.OnKeyDown(null, 10); // 'C'
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 200);
        tracker.OnKeyDown(null, 210); // 'V'
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void KeyRepeatWhileHeld_DoesNotResetChordFlagOrDuplicateDownTick()
    {
        // Windows fires repeated WM_KEYDOWN for an auto-repeating held key. These must be no-ops
        // against an already-down key, and they must not clear WasChorded or move DownTick forward.
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);
        tracker.OnKeyDown(null, 10); // chord: 'C' down.
        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 20); // autorepeat of the still-held Ctrl.
        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 30); // more autorepeat.
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(0, fireCount); // still chorded, autorepeat must not have cleared it.

        // State wasn't corrupted by the autorepeat: a clean tap now, then a real double-tap,
        // still behaves normally.
        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 100);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(0, fireCount);

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 150);
        tracker.OnKeyUp(NativeMethods.VK_LCONTROL);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void BothCtrlKeysHeldSimultaneously_EachCountsOnlyTowardOwnDetector()
    {
        // Holding Left while Right taps must not chord-poison Right (and vice versa). A
        // tracked key going down only poisons OTHER tracked keys' bookkeeping, it doesn't touch
        // the incoming key's own state, and only an untracked key poisons at all.
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        tracker.OnKeyDown(NativeMethods.VK_LCONTROL, 0);

        tracker.OnKeyDown(NativeMethods.VK_RCONTROL, 10);
        tracker.OnKeyUp(NativeMethods.VK_RCONTROL);
        tracker.OnKeyDown(NativeMethods.VK_RCONTROL, 60);
        tracker.OnKeyUp(NativeMethods.VK_RCONTROL);

        Assert.Equal(1, fireCount); // Right's own pair fired.

        tracker.OnKeyUp(NativeMethods.VK_LCONTROL); // Left's long single hold releases.

        Assert.Equal(1, fireCount); // Left never paired with anything, so still just the one fire.
    }

    [Fact]
    public void RapidFiveTaps_FiresExactlyOncePerQualifyingPair()
    {
        CtrlKeyTracker tracker = CreateTracker();
        int fireCount = 0;
        tracker.DoubleTapDetected += () => fireCount++;

        long[] downTicks = [0, 100, 200, 300, 400];
        var fired = new List<bool>();

        foreach (long tick in downTicks)
        {
            int before = fireCount;
            tracker.OnKeyDown(NativeMethods.VK_LCONTROL, tick);
            tracker.OnKeyUp(NativeMethods.VK_LCONTROL);
            fired.Add(fireCount > before);
        }

        Assert.Equal([false, true, false, true, false], fired);
    }
}
