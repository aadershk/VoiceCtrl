using VoiceCtrl.Core.Hotkey;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class TriggerKeyTrackerTests
{
    private const int Vk = 0x70; // arbitrary stand-in VK, e.g. F1
    private const int ChordSecondVk = 0x20; // arbitrary stand-in, e.g. Space
    private const long ThresholdMs = 200;

    private static TriggerKeyTracker CreateSingleKey() => new(Vk, secondVk: null, ThresholdMs);

    private static TriggerKeyTracker CreateChord() => new(Vk, ChordSecondVk, ThresholdMs);

    [Fact]
    public void SingleKey_QuickPressRelease_FiresTapped()
    {
        TriggerKeyTracker tracker = CreateSingleKey();
        int tapCount = 0, holdStartCount = 0, holdEndCount = 0;
        tracker.Tapped += () => tapCount++;
        tracker.HoldStarted += () => holdStartCount++;
        tracker.HoldEnded += () => holdEndCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyUp(Vk, 50);

        Assert.Equal(1, tapCount);
        Assert.Equal(0, holdStartCount);
        Assert.Equal(0, holdEndCount);
    }

    [Fact]
    public void SingleKey_EngagesImmediatelyOnKeyDown()
    {
        TriggerKeyTracker tracker = CreateSingleKey();

        tracker.OnKeyDown(Vk, 0);

        Assert.True(tracker.IsEngaged);
    }

    [Fact]
    public void SingleKey_HeldPastThreshold_FiresHoldStartedNotTapped()
    {
        TriggerKeyTracker tracker = CreateSingleKey();
        int tapCount = 0, holdStartCount = 0;
        tracker.Tapped += () => tapCount++;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.CheckHoldThreshold(250);

        Assert.Equal(1, holdStartCount);
        Assert.Equal(0, tapCount);
    }

    [Fact]
    public void SingleKey_CheckBeforeThresholdElapsed_DoesNotFireHoldStarted()
    {
        TriggerKeyTracker tracker = CreateSingleKey();
        int holdStartCount = 0;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.CheckHoldThreshold(100);

        Assert.Equal(0, holdStartCount);
    }

    [Fact]
    public void SingleKey_ReleaseAfterHoldStarted_FiresHoldEndedNotTapped()
    {
        TriggerKeyTracker tracker = CreateSingleKey();
        int tapCount = 0, holdEndCount = 0;
        tracker.Tapped += () => tapCount++;
        tracker.HoldEnded += () => holdEndCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.CheckHoldThreshold(250);
        tracker.OnKeyUp(Vk, 400);

        Assert.Equal(1, holdEndCount);
        Assert.Equal(0, tapCount);
    }

    [Fact]
    public void SingleKey_KeyRepeatWhileHeld_DoesNotResetEngagementClock()
    {
        TriggerKeyTracker tracker = CreateSingleKey();
        int holdStartCount = 0;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(Vk, 50); // OS autorepeat of the still-held key
        tracker.OnKeyDown(Vk, 100);

        // If a repeat had reset the engagement clock to 100, this check (250) would be only 150ms
        // past it and should not fire yet. It must fire because engagement is still anchored at 0.
        tracker.CheckHoldThreshold(250);

        Assert.Equal(1, holdStartCount);
    }

    [Fact]
    public void SingleKey_StaleCheckAfterRelease_IsNoOp()
    {
        TriggerKeyTracker tracker = CreateSingleKey();
        int holdStartCount = 0;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyUp(Vk, 50); // released as a tap, well before the threshold

        // A timer armed at engagement can still fire after release if the caller didn't cancel it.
        tracker.CheckHoldThreshold(250);

        Assert.Equal(0, holdStartCount);
    }

    [Fact]
    public void SingleKey_UnrelatedVk_IsIgnored()
    {
        TriggerKeyTracker tracker = CreateSingleKey();
        int tapCount = 0;
        tracker.Tapped += () => tapCount++;

        tracker.OnKeyDown(0x41, 0);
        tracker.OnKeyUp(0x41, 10);

        Assert.False(tracker.IsEngaged);
        Assert.Equal(0, tapCount);
    }

    [Fact]
    public void Chord_PrefixAlone_DoesNotEngage()
    {
        TriggerKeyTracker tracker = CreateChord();

        tracker.OnKeyDown(Vk, 0);

        Assert.False(tracker.IsEngaged);
    }

    [Fact]
    public void Chord_PrefixThenSecondKey_Engages()
    {
        TriggerKeyTracker tracker = CreateChord();

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(ChordSecondVk, 10);

        Assert.True(tracker.IsEngaged);
    }

    [Fact]
    public void Chord_QuickReleaseOfSecondKey_FiresTapped()
    {
        TriggerKeyTracker tracker = CreateChord();
        int tapCount = 0;
        tracker.Tapped += () => tapCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(ChordSecondVk, 10);
        tracker.OnKeyUp(ChordSecondVk, 60);

        Assert.Equal(1, tapCount);
    }

    [Fact]
    public void Chord_ReleasingPrefixWhileHolding_EndsIt()
    {
        TriggerKeyTracker tracker = CreateChord();
        int holdEndCount = 0;
        tracker.HoldEnded += () => holdEndCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(ChordSecondVk, 10);
        tracker.CheckHoldThreshold(300);
        tracker.OnKeyUp(Vk, 400); // releasing the PREFIX, not the second key, still ends it

        Assert.Equal(1, holdEndCount);
    }

    [Fact]
    public void Chord_ReEngagingAfterRelease_StartsAFreshEngagementClock()
    {
        TriggerKeyTracker tracker = CreateChord();
        int tapCount = 0, holdStartCount = 0;
        tracker.Tapped += () => tapCount++;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(ChordSecondVk, 10);
        tracker.OnKeyUp(ChordSecondVk, 60); // tap #1

        tracker.OnKeyDown(ChordSecondVk, 600); // re-press second key, prefix still down
        tracker.CheckHoldThreshold(900); // 300ms since the second engagement, past the threshold

        Assert.Equal(1, tapCount);
        Assert.Equal(1, holdStartCount);
    }
}
