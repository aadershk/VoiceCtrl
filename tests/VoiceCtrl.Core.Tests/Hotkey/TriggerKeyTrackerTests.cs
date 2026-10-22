using VoiceCtrl.Core.Hotkey;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class TriggerKeyTrackerTests
{
    private const int Vk = 0x70; // arbitrary stand-in VK, e.g. F1
    private const int ChordSecondVk = 0x20; // arbitrary stand-in, e.g. Space
    private const long ThresholdMs = 200;

    private static TriggerKeyTracker CreateSingleKey(Func<int, bool>? isKeyActuallyDown = null) =>
        new(Vk, secondVk: null, ThresholdMs, isKeyActuallyDown);

    private static TriggerKeyTracker CreateChord(Func<int, bool>? isKeyActuallyDown = null) =>
        new(Vk, ChordSecondVk, ThresholdMs, isKeyActuallyDown);

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

    [Fact]
    public void Chord_StaleSecondKeyFlagFromMissedKeyUp_DoesNotFalselyEngageOnUnrelatedPrefixPress()
    {
        // Simulates Windows silently dropping a key-up for the chord's second key (most reliably
        // reproduced by opening the Start Menu and dismissing it by clicking elsewhere rather than
        // pressing Win or Esc again): OnKeyUp never arrives, so the internal flag is stuck "down"
        // even though the key has genuinely been released. The injected delegate is the tracker's
        // only way to learn that, since no further key event for it will ever come.
        bool secondKeyActuallyDown = true;
        TriggerKeyTracker tracker = CreateChord(isKeyActuallyDown: vk => vk != ChordSecondVk || secondKeyActuallyDown);

        tracker.OnKeyDown(ChordSecondVk, 0);
        Assert.False(tracker.IsEngaged); // second key alone never engages a chord.

        secondKeyActuallyDown = false; // the missed key-up: physically released, but no OnKeyUp call ever arrives.

        // Later, an unrelated press of just the prefix (e.g. plain Ctrl during copy/paste) must not
        // be read as completing the chord via the stale second-key flag.
        tracker.OnKeyDown(Vk, 500);

        Assert.False(tracker.IsEngaged);
    }

    [Fact]
    public void Chord_StalePrefixFlagFromMissedKeyUp_DoesNotFalselyEngageOnUnrelatedSecondKeyPress()
    {
        // Same failure mode, mirrored onto the prefix key: a missed key-up leaves the prefix's
        // flag stuck "down", and a later unrelated press of just the second key must not be
        // misread as completing the chord.
        bool prefixActuallyDown = true;
        TriggerKeyTracker tracker = CreateChord(isKeyActuallyDown: vk => vk != Vk || prefixActuallyDown);

        tracker.OnKeyDown(Vk, 0);
        prefixActuallyDown = false; // the missed key-up: physically released, but no OnKeyUp call ever arrives.

        tracker.OnKeyDown(ChordSecondVk, 500);

        Assert.False(tracker.IsEngaged);
    }

    [Fact]
    public void Chord_BothKeysGenuinelyDownPerInjectedState_StillEngagesNormally()
    {
        // The injected real-state check must not interfere with a genuine engagement: when it
        // confirms both keys are actually down, the chord engages exactly as it would without it.
        TriggerKeyTracker tracker = CreateChord(isKeyActuallyDown: _ => true);

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(ChordSecondVk, 10);

        Assert.True(tracker.IsEngaged);
    }

    [Fact]
    public void Chord_GenuineTapWithInjectedRealStateCheck_StillFiresTapped()
    {
        // The injected real-state check must not interfere with a genuine tap: Tapped still fires
        // on a quick release exactly as it would without the collaborator wired up.
        TriggerKeyTracker tracker = CreateChord(isKeyActuallyDown: _ => true);
        int tapCount = 0;
        tracker.Tapped += () => tapCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(ChordSecondVk, 10);
        tracker.OnKeyUp(ChordSecondVk, 60);

        Assert.Equal(1, tapCount);
    }

    [Fact]
    public void Chord_GenuineHoldWithInjectedRealStateCheck_StillFiresHoldStartedAndEnded()
    {
        // Same, for the Hold-to-Talk path: HoldStarted/HoldEnded still fire exactly as without
        // the collaborator wired up.
        TriggerKeyTracker tracker = CreateChord(isKeyActuallyDown: _ => true);
        int holdStartCount = 0, holdEndCount = 0;
        tracker.HoldStarted += () => holdStartCount++;
        tracker.HoldEnded += () => holdEndCount++;

        tracker.OnKeyDown(Vk, 0);
        tracker.OnKeyDown(ChordSecondVk, 10);
        tracker.CheckHoldThreshold(300);
        tracker.OnKeyUp(Vk, 400);

        Assert.Equal(1, holdStartCount);
        Assert.Equal(1, holdEndCount);
    }

    [Fact]
    public void SingleKey_UnaffectedByInjectedRealStateCheck()
    {
        // A single-key Trigger Key has no "other" chord key to cross-check, so wiring up the
        // delegate must not change its existing, already-correct behavior.
        TriggerKeyTracker tracker = CreateSingleKey(isKeyActuallyDown: _ => false);

        tracker.OnKeyDown(Vk, 0);

        Assert.True(tracker.IsEngaged);
    }

    private const int ModA = 0xA2; // stand-in for Left Ctrl
    private const int ModB = 0x5B; // stand-in for Left Win
    private const int KeyC = 0x41;

    private static TriggerKeyTracker CreateCombination(params int[] vks) =>
        new(vks, ThresholdMs, isKeyActuallyDown: null);

    [Fact]
    public void Combination_TwoModifiers_EngagesOnlyWhenBothDown_InEitherOrder()
    {
        foreach (int[] order in new[] { new[] { ModA, ModB }, new[] { ModB, ModA } })
        {
            TriggerKeyTracker tracker = CreateCombination(ModA, ModB);

            tracker.OnKeyDown(order[0], 0);
            Assert.False(tracker.IsEngaged);
            tracker.OnKeyDown(order[1], 10);
            Assert.True(tracker.IsEngaged);
        }
    }

    [Fact]
    public void Combination_ThreeKeys_NeedsAllThree_AndAnyReleaseEndsTheHold()
    {
        TriggerKeyTracker tracker = CreateCombination(ModA, ModB, KeyC);
        int holdStartCount = 0, holdEndCount = 0;
        tracker.HoldStarted += () => holdStartCount++;
        tracker.HoldEnded += () => holdEndCount++;

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyDown(ModB, 5);
        Assert.False(tracker.IsEngaged);
        tracker.OnKeyDown(KeyC, 10);
        Assert.True(tracker.IsEngaged);
        tracker.CheckHoldThreshold(300);
        tracker.OnKeyUp(ModB, 400);

        Assert.Equal(1, holdStartCount);
        Assert.Equal(1, holdEndCount);
        Assert.False(tracker.IsEngaged);
    }

    [Fact]
    public void Combination_BriefAccidentalOverlap_IsATapNotAHold()
    {
        TriggerKeyTracker tracker = CreateCombination(ModA, ModB);
        int tapCount = 0, holdStartCount = 0;
        tracker.Tapped += () => tapCount++;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyDown(ModB, 10);
        tracker.CheckHoldThreshold(60);
        tracker.OnKeyUp(ModA, 80);

        Assert.Equal(0, holdStartCount);
        Assert.Equal(1, tapCount);
    }

    [Fact]
    public void Combination_StaleKeyFlag_IsCorrectedAgainstRealKeyState()
    {
        TriggerKeyTracker tracker = new([ModA, ModB], ThresholdMs, isKeyActuallyDown: _ => false);

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyUp(ModB, 5); // a missed key-up elsewhere must not leave ModA counting as down
        tracker.OnKeyDown(ModB, 1000);

        Assert.False(tracker.IsEngaged);
    }

    // Ctrl + Shift is also the first half of Ctrl + Shift + T and a dozen other shortcuts (VED-60).

    [Fact]
    public void Combination_AnotherKeyBeforeTheHoldThreshold_IsAShortcutNotAHold()
    {
        TriggerKeyTracker tracker = new([ModA, ModB], ThresholdMs, otherKeySpoilsHold: true);
        int tapCount = 0, holdStartCount = 0, holdEndCount = 0;
        tracker.Tapped += () => tapCount++;
        tracker.HoldStarted += () => holdStartCount++;
        tracker.HoldEnded += () => holdEndCount++;

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyDown(ModB, 10);
        tracker.OnKeyDown(0x54, 40); // T
        tracker.CheckHoldThreshold(400);
        tracker.OnKeyUp(0x54, 450);
        tracker.OnKeyUp(ModB, 460);
        tracker.OnKeyUp(ModA, 470);

        Assert.Equal(0, holdStartCount);
        Assert.Equal(0, holdEndCount);
        Assert.Equal(0, tapCount);
    }

    [Fact]
    public void Combination_AnotherKeyAfterTheHoldStarted_CancelsTheHoldInsteadOfEndingIt()
    {
        TriggerKeyTracker tracker = new([ModA, ModB], ThresholdMs, otherKeySpoilsHold: true);
        int holdStartCount = 0, holdEndCount = 0, cancelCount = 0;
        tracker.HoldStarted += () => holdStartCount++;
        tracker.HoldEnded += () => holdEndCount++;
        tracker.HoldCancelled += () => cancelCount++;

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyDown(ModB, 10);
        tracker.CheckHoldThreshold(300);
        tracker.OnKeyDown(0x54, 350); // Ctrl + Shift + T after thinking for a moment: a shortcut after all
        tracker.OnKeyDown(0x55, 360);
        tracker.OnKeyUp(ModB, 500);

        Assert.Equal(1, holdStartCount);
        Assert.Equal(1, cancelCount); // once, however many keys follow
        Assert.Equal(0, holdEndCount); // the release of a cancelled hold is not "stop and transcribe"
    }

    [Fact]
    public void Combination_WithoutTheSpoilRule_AnotherKeyAfterTheHoldStarted_DoesNotEndOrCancelIt()
    {
        TriggerKeyTracker tracker = CreateCombination(ModA, ModB);
        int holdEndCount = 0, cancelCount = 0;
        tracker.HoldEnded += () => holdEndCount++;
        tracker.HoldCancelled += () => cancelCount++;

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyDown(ModB, 10);
        tracker.CheckHoldThreshold(300);
        tracker.OnKeyDown(0x54, 350);
        tracker.OnKeyUp(ModB, 500);

        Assert.Equal(0, cancelCount);
        Assert.Equal(1, holdEndCount);
    }

    [Fact]
    public void Combination_AfterAShortcut_TheNextCleanHoldStillWorks()
    {
        TriggerKeyTracker tracker = new([ModA, ModB], ThresholdMs, otherKeySpoilsHold: true);
        int holdStartCount = 0;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyDown(ModB, 10);
        tracker.OnKeyDown(0x54, 40);
        tracker.OnKeyUp(ModB, 100);
        tracker.OnKeyUp(ModA, 110);

        tracker.OnKeyDown(ModA, 1000);
        tracker.OnKeyDown(ModB, 1010);
        tracker.CheckHoldThreshold(1300);

        Assert.Equal(1, holdStartCount);
    }

    [Fact]
    public void Combination_WithoutTheSpoilRule_AnotherKeyDoesNotChangeTheGesture()
    {
        TriggerKeyTracker tracker = CreateCombination(ModA, ModB);
        int holdStartCount = 0;
        tracker.HoldStarted += () => holdStartCount++;

        tracker.OnKeyDown(ModA, 0);
        tracker.OnKeyDown(ModB, 10);
        tracker.OnKeyDown(0x54, 40);
        tracker.CheckHoldThreshold(400);

        Assert.Equal(1, holdStartCount);
    }

    [Fact]
    public void ZeroThreshold_Combination_StartsTheHoldOnTheKeyPress_AndReleaseOnlyEndsIt()
    {
        var tracker = new TriggerKeyTracker([ModA, ModB], holdThresholdMs: 0);
        int tapCount = 0, holdStartCount = 0, holdEndCount = 0;
        tracker.Tapped += () => tapCount++;
        tracker.HoldStarted += () => holdStartCount++;
        tracker.HoldEnded += () => holdEndCount++;

        tracker.OnKeyDown(ModA, 0);
        Assert.Equal(0, holdStartCount);
        tracker.OnKeyDown(ModB, 10);
        Assert.Equal(1, holdStartCount);

        tracker.OnKeyUp(ModB, 60);

        Assert.Equal(1, holdEndCount);
        Assert.Equal(0, tapCount);
    }
}
