using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class KeyCaptureTests
{
    private const int VkA = 0x41;
    private const int VkV = 0x56;
    private const int VkSpace = 0x20;
    private const int VkEscape = 0x1B;

    private static readonly DictationSection Defaults = new();

    /// <summary>Presses the keys in order, then releases them in reverse, as fingers do.</summary>
    private static KeyCaptureStep Press(KeyCapture capture, params int[] vks)
    {
        foreach (int vk in vks)
        {
            Assert.Equal(KeyCaptureStatus.Waiting, capture.KeyDown(vk).Status);
        }

        KeyCaptureStep step = new(KeyCaptureStatus.Waiting);
        for (int i = vks.Length - 1; i >= 0; i--)
        {
            step = capture.KeyUp(vks[i]);
            if (i > 0)
            {
                Assert.Equal(KeyCaptureStatus.Waiting, step.Status);
            }
        }

        return step;
    }

    private static DictationSection WithTrigger(string key) =>
        Defaults with { Hotkeys = Defaults.Hotkeys with { TriggerEnabled = true, TriggerMode = HotkeySettings.TriggerModeSingle, TriggerKey = key } };

    private static DictationSection WithChordTrigger(string prefix, int secondVk, string label) =>
        Defaults with
        {
            Hotkeys = Defaults.Hotkeys with
            {
                TriggerEnabled = true,
                TriggerMode = HotkeySettings.TriggerModeChord,
                ChordPrefix = prefix,
                ChordSecondKeyVk = secondVk,
                ChordSecondKeyLabel = label,
            },
        };

    [Fact]
    public void Escape_CancelsAtOnce_ForEveryTarget()
    {
        foreach (KeyCaptureTarget target in Enum.GetValues<KeyCaptureTarget>())
        {
            var capture = new KeyCapture(target, Defaults);
            Assert.Equal(KeyCaptureStatus.Cancelled, capture.KeyDown(VkEscape).Status);
        }
    }

    [Fact]
    public void Hotkey_SingleCatalogKey_IsSavedOnRelease()
    {
        var capture = new KeyCapture(KeyCaptureTarget.Hotkey, Defaults);

        Assert.Equal(KeyCaptureStatus.Waiting, capture.KeyDown(NativeMethods.VK_F8).Status);
        KeyCaptureStep step = capture.KeyUp(NativeMethods.VK_F8);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal("F8", step.Result!.Hotkeys.HotkeyKey);
    }

    [Theory]
    [InlineData(NativeMethods.VK_LCONTROL)]
    [InlineData(NativeMethods.VK_RCONTROL)]
    public void Hotkey_EitherCtrl_SavesPlainCtrl(int vk)
    {
        // "Ctrl" is either side, as the default always was: re-choosing Ctrl must not lose one side.
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.Hotkey, Defaults), vk);

        Assert.Equal(HotkeyKeyCatalog.DefaultHotkeyKey, step.Result!.Hotkeys.HotkeyKey);
    }

    [Fact]
    public void Hotkey_KeyYouTypeWith_GivesAHintAndKeepsWaiting()
    {
        var capture = new KeyCapture(KeyCaptureTarget.Hotkey, Defaults);

        KeyCaptureStep hint = Press(capture, VkA);
        KeyCaptureStep next = Press(capture, NativeMethods.VK_CAPITAL);

        Assert.Equal(KeyCaptureStatus.Hint, hint.Status);
        Assert.Equal(KeyCapture.HotkeyNotAllowedHint, hint.Hint);
        Assert.Equal(KeyCaptureStatus.Done, next.Status);
        Assert.Equal("Caps Lock", next.Result!.Hotkeys.HotkeyKey);
    }

    [Fact]
    public void Hotkey_TwoKeysTogether_AsksForJustOne()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.Hotkey, Defaults), NativeMethods.VK_LCONTROL, VkA);

        Assert.Equal(KeyCaptureStatus.Hint, step.Status);
        Assert.Equal(KeyCapture.HotkeyOneKeyHint, step.Hint);
    }

    [Fact]
    public void Hotkey_SameKeyAsTheTriggerKey_IsRefused()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.Hotkey, WithTrigger("F9")), NativeMethods.VK_F9);

        Assert.Equal(KeyCaptureStatus.Hint, step.Status);
        Assert.Equal(KeyCapture.IsTriggerKeyHint, step.Hint);
    }

    [Fact]
    public void Hotkey_PlainCtrl_ClashesWithAOneSidedCtrlTriggerKey()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.Hotkey, WithTrigger("Right Ctrl")), NativeMethods.VK_LCONTROL);

        Assert.Equal(KeyCapture.IsTriggerKeyHint, step.Hint);
    }

    [Fact]
    public void Hotkey_KnownReservedKey_IsSavedWithItsWarning()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.Hotkey, Defaults), NativeMethods.VK_LSHIFT);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal(HotkeyKeyCatalog.WarningFor("Left Shift"), step.Warning);
    }

    [Fact]
    public void Hotkey_AutoRepeatWhileHeld_CountsAsOnePress()
    {
        var capture = new KeyCapture(KeyCaptureTarget.Hotkey, Defaults);

        capture.KeyDown(NativeMethods.VK_F8);
        capture.KeyDown(NativeMethods.VK_F8);
        capture.KeyDown(NativeMethods.VK_F8);

        Assert.Equal(KeyCaptureStatus.Done, capture.KeyUp(NativeMethods.VK_F8).Status);
    }

    [Fact]
    public void AReleaseWithoutAPress_IsIgnored()
    {
        // The Enter that clicked "Change" goes down before the capture starts and comes up inside it.
        var capture = new KeyCapture(KeyCaptureTarget.Hotkey, Defaults);

        Assert.Equal(KeyCaptureStatus.Waiting, capture.KeyUp(0x0D).Status);
        Assert.Equal(KeyCaptureStatus.Done, Press(capture, NativeMethods.VK_F8).Status);
    }

    [Fact]
    public void TriggerKey_SingleKey_TurnsItOnInSingleMode()
    {
        // With the Hotkey on Left Ctrl only, Right Ctrl is free.
        DictationSection current = Defaults with { Hotkeys = Defaults.Hotkeys with { HotkeyKey = "Left Ctrl" } };

        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, current), NativeMethods.VK_RCONTROL);

        HotkeySettings hotkeys = step.Result!.Hotkeys;
        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.True(hotkeys.IsTriggerReady);
        Assert.Equal(HotkeySettings.TriggerModeSingle, hotkeys.TriggerMode);
        Assert.Equal("Right Ctrl", hotkeys.TriggerKey);
        Assert.Null(hotkeys.ChordPrefix);
    }

    [Fact]
    public void TriggerKey_ModifierHeldWithAnotherKey_IsAChord()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, WithTrigger("F9")), NativeMethods.VK_LMENU, VkSpace);

        HotkeySettings hotkeys = step.Result!.Hotkeys;
        Assert.Equal(HotkeySettings.TriggerModeChord, hotkeys.TriggerMode);
        Assert.Equal("Left Alt", hotkeys.ChordPrefix);
        Assert.Equal(VkSpace, hotkeys.ChordSecondKeyVk);
        Assert.Equal("Space", hotkeys.ChordSecondKeyLabel);
        Assert.Null(hotkeys.TriggerKey);
    }

    [Fact]
    public void TriggerKey_Chord_CarriesTheKnownChordWarning()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), NativeMethods.VK_LCONTROL, VkSpace);

        Assert.Equal(HotkeyKeyCatalog.ChordWarningFor("Left Ctrl", "Space"), step.Warning);
    }

    [Fact]
    public void TriggerKey_TwoModifiers_IsACombination()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), NativeMethods.VK_LCONTROL, NativeMethods.VK_LWIN);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        HotkeySettings hotkeys = step.Result!.Hotkeys;
        Assert.True(hotkeys.TriggerEnabled);
        Assert.Equal(HotkeySettings.TriggerModeCombination, hotkeys.TriggerMode);
        Assert.Equal([NativeMethods.VK_LCONTROL, NativeMethods.VK_LWIN], hotkeys.ComboVks);
        Assert.Equal(["Left Ctrl", "Left Win"], hotkeys.ComboLabels);
    }

    [Fact]
    public void TriggerKey_ThreeKeys_ModifiersPlusOneKey_IsACombination()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), NativeMethods.VK_LCONTROL, NativeMethods.VK_LSHIFT, VkA);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal(3, step.Result!.Hotkeys.ComboVks!.Count);
    }

    [Fact]
    public void TriggerKey_FourKeys_OrTwoPlainKeys_GiveTheHint()
    {
        KeyCaptureStep four = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults),
            NativeMethods.VK_LCONTROL, NativeMethods.VK_LSHIFT, NativeMethods.VK_LMENU, VkA);
        KeyCaptureStep plain = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), VkA, VkV);

        Assert.Equal(KeyCapture.TriggerKeyHint, four.Hint);
        Assert.Equal(KeyCapture.TriggerKeyHint, plain.Hint);
    }

    [Theory]
    [InlineData(NativeMethods.VK_LWIN, 0x4C)] // Win+L locks the PC
    [InlineData(NativeMethods.VK_RWIN, 0x44)] // Win+D shows the desktop
    public void TriggerKey_CombinationWindowsOwns_IsRefused(int winVk, int otherVk)
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), winVk, otherVk);

        Assert.Equal(KeyCaptureStatus.Hint, step.Status);
        Assert.Equal(KeyCapture.WindowsOwnsHint, step.Hint);
    }

    [Fact]
    public void TriggerKey_CtrlShift_WarnsAboutTheLanguageSwitch()
    {
        // Ctrl + Shift is the Rewrite key by default, so it is free for the Trigger Key only once that is removed.
        DictationSection noRewrite = Defaults with { Hotkeys = Defaults.Hotkeys with { RewriteEnabled = false } };

        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, noRewrite), NativeMethods.VK_LCONTROL, NativeMethods.VK_LSHIFT);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal(HotkeyKeyCatalog.LanguageSwitchWarning, step.Warning);
    }

    [Fact]
    public void TriggerKey_TheHotkeyAlone_IsRefused()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), NativeMethods.VK_LCONTROL);

        Assert.Equal(KeyCapture.IsHotkeyHint, step.Hint);
    }

    [Theory]
    [InlineData(new[] { VkA })]
    [InlineData(new[] { NativeMethods.VK_F8, VkA })]
    public void TriggerKey_AnythingElse_GivesTheHint(int[] vks)
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), vks);

        Assert.Equal(KeyCapture.TriggerKeyHint, step.Hint);
    }

    [Fact]
    public void TriggerKey_ChordThatIsThePasteAgainKey_IsRefused()
    {
        DictationSection current = Defaults with { PasteAgain = new PasteAgainKey { Modifiers = ["Alt"], KeyVk = VkV, KeyLabel = "V" } };

        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, current), NativeMethods.VK_RMENU, VkV);

        Assert.Equal(KeyCapture.IsPasteAgainHint, step.Hint);
    }

    [Fact]
    public void PasteAgain_ModifiersAndAKey_AreSavedInWindowsOrder()
    {
        KeyCaptureStep step = Press(
            new KeyCapture(KeyCaptureTarget.PasteAgain, Defaults),
            NativeMethods.VK_LSHIFT, NativeMethods.VK_RCONTROL, NativeMethods.VK_LMENU, 0x42);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal(["Ctrl", "Alt", "Shift", "B"], step.Result!.PasteAgain.Chips);
        Assert.Equal(0x42, step.Result.PasteAgain.KeyVk);
    }

    [Theory]
    [InlineData(new[] { VkV })]
    [InlineData(new[] { NativeMethods.VK_LSHIFT, VkV })]
    [InlineData(new[] { NativeMethods.VK_LCONTROL })]
    [InlineData(new[] { NativeMethods.VK_LCONTROL, VkV, 0x42 })]
    public void PasteAgain_NeedsCtrlAltOrWinAndOneKey(int[] vks)
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.PasteAgain, Defaults), vks);

        Assert.Equal(KeyCapture.PasteAgainHint, step.Hint);
    }

    [Fact]
    public void PasteAgain_CtrlV_IsRefused()
    {
        // VoiceCtrl pastes by sending Ctrl+V; as the Paste Again key it would set itself off.
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.PasteAgain, Defaults), NativeMethods.VK_LCONTROL, VkV);

        Assert.Equal(KeyCapture.IsPasteHint, step.Hint);
    }

    [Fact]
    public void PasteAgain_TheTriggerKeyChord_IsRefused()
    {
        DictationSection current = WithChordTrigger("Left Ctrl", VkSpace, "Space");

        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.PasteAgain, current), NativeMethods.VK_RCONTROL, VkSpace);

        Assert.Equal(KeyCapture.IsTriggerKeyHint, step.Hint);
    }

    [Fact]
    public void TakenHint_NamesTheChord()
    {
        var key = new PasteAgainKey { Modifiers = ["Ctrl", "Alt"], KeyVk = VkV, KeyLabel = "V" };

        Assert.Equal("Another app already uses Ctrl + Alt + V. Try another.", KeyCapture.TakenHint(key));
    }

    [Fact]
    public void Hotkey_OnFreshSettings_ComparesAgainstTheDefaultTriggerWithoutFailing()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.Hotkey, Defaults), HotkeyKeyCatalog.ResolveVkCodes("F8")[0]);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal("F8", step.Result!.Hotkeys.HotkeyKey);
    }

    [Fact]
    public void PasteAgain_OnFreshSettings_ComparesAgainstTheDefaultTriggerWithoutFailing()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.PasteAgain, Defaults), NativeMethods.VK_LCONTROL, NativeMethods.VK_LMENU, VkA);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
    }

    [Fact]
    public void RewriteKey_TheDefaultTriggerKeys_AreRefused()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, Defaults), NativeMethods.VK_LWIN, NativeMethods.VK_LCONTROL);

        Assert.Equal(KeyCaptureStatus.Hint, step.Status);
        Assert.Equal(KeyCapture.IsTriggerKeyHint, step.Hint);
    }

    [Fact]
    public void TriggerKey_ChosenAfterRemoving_TurnsItBackOn()
    {
        DictationSection removed = Defaults with { Hotkeys = Defaults.Hotkeys with { TriggerRemoved = true } };

        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, removed), NativeMethods.VK_LCONTROL, NativeMethods.VK_LMENU);

        Assert.True(step.Result!.Hotkeys.IsTriggerReady);
        Assert.Equal(["Left Ctrl", "Left Alt"], step.Result.Hotkeys.TriggerChips);
    }

    [Fact]
    public void TriggerKey_SingleKeyAndChordChosenAfterRemoving_TurnItBackOn()
    {
        DictationSection removed = Defaults with { Hotkeys = Defaults.Hotkeys with { TriggerRemoved = true } };

        KeyCaptureStep single = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, removed), HotkeyKeyCatalog.ResolveVkCodes("F9")[0]);
        KeyCaptureStep chord = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, removed), NativeMethods.VK_LMENU, VkSpace);

        Assert.True(single.Result!.Hotkeys.IsTriggerReady);
        Assert.True(chord.Result!.Hotkeys.IsTriggerReady);
    }

    [Fact]
    public void RewriteKey_AcceptsACombinationAndKeepsItsLabels()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, Defaults), NativeMethods.VK_LCONTROL, NativeMethods.VK_LMENU);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        HotkeySettings saved = step.Result!.Hotkeys;
        Assert.True(saved.RewriteEnabled);
        Assert.Equal([NativeMethods.VK_LCONTROL, NativeMethods.VK_LMENU], saved.RewriteKeys);
        Assert.Equal(["Left Ctrl", "Left Alt"], saved.RewriteChips);
    }

    [Fact]
    public void RewriteKey_AcceptsOneCatalogKey()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, Defaults), HotkeyKeyCatalog.ResolveVkCodes("F9")[0]);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal(["F9"], step.Result!.Hotkeys.RewriteChips);
    }

    [Fact]
    public void RewriteKey_CtrlShift_SavesWithTheLanguageSwitchWarning()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, Defaults), NativeMethods.VK_LCONTROL, NativeMethods.VK_LSHIFT);

        Assert.Equal(KeyCaptureStatus.Done, step.Status);
        Assert.Equal(HotkeyKeyCatalog.LanguageSwitchWarning, step.Warning);
    }

    [Fact]
    public void RewriteKey_TwoPlainLetters_AreRefused()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, Defaults), VkA, VkV);

        Assert.Equal(KeyCapture.TriggerKeyHint, step.Hint);
    }

    [Fact]
    public void RewriteKey_WindowsOwnedCombination_IsRefused()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, Defaults), NativeMethods.VK_LWIN, 0x4C);

        Assert.Equal(KeyCapture.WindowsOwnsHint, step.Hint);
    }

    [Fact]
    public void RewriteKey_TheSameKeysAsTheTriggerKey_AreRefusedWhateverTheOrder()
    {
        DictationSection current = Defaults with
        {
            Hotkeys = Defaults.Hotkeys with
            {
                TriggerEnabled = true,
                TriggerMode = HotkeySettings.TriggerModeCombination,
                ComboVks = [NativeMethods.VK_LCONTROL, NativeMethods.VK_LWIN],
            },
        };

        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, current), NativeMethods.VK_LWIN, NativeMethods.VK_LCONTROL);

        Assert.Equal(KeyCapture.IsTriggerKeyHint, step.Hint);
    }

    [Fact]
    public void TriggerKey_TheSameKeysAsTheRewriteKey_AreRefused()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.TriggerKey, Defaults), NativeMethods.VK_LSHIFT, NativeMethods.VK_LCONTROL);

        Assert.Equal(KeyCapture.IsRewriteKeyHint, step.Hint);
    }

    [Fact]
    public void RewriteKey_TheHotkeyItself_IsRefused()
    {
        KeyCaptureStep step = Press(new KeyCapture(KeyCaptureTarget.RewriteKey, Defaults), NativeMethods.VK_LCONTROL);

        Assert.Equal(KeyCapture.IsHotkeyHint, step.Hint);
    }

    [Fact]
    public void Reset_StartsAFreshGesture()
    {
        var capture = new KeyCapture(KeyCaptureTarget.PasteAgain, Defaults);
        capture.KeyDown(NativeMethods.VK_LCONTROL);

        capture.Reset();

        Assert.Equal(KeyCaptureStatus.Done, Press(capture, NativeMethods.VK_LMENU, VkV).Status);
    }
}
