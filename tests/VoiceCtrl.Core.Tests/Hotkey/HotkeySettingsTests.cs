using System.Text.Json;
using VoiceCtrl.Core.Hotkey;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

/// <summary>Persistence lives in the settings store now (SettingsStoreTests round-trips these
/// bindings); what stays here is the readiness rule the keyboard hook starts from.</summary>
public class HotkeySettingsTests
{
    [Fact]
    public void Defaults_AreTheCtrlDoubleTapPlusHoldLeftCtrlLeftWinToTalkAndLeftCtrlLeftShiftToRewrite()
    {
        var settings = new HotkeySettings();

        Assert.Equal(HotkeyKeyCatalog.DefaultHotkeyKey, settings.HotkeyKey);
        Assert.True(settings.IsTriggerReady);
        Assert.Equal([0xA2, 0x5B], settings.TriggerVks);
        Assert.Equal(["Left Ctrl", "Left Win"], settings.TriggerChips);
        Assert.Equal([0xA2, 0xA0], settings.RewriteKeys);
        Assert.Equal(["Left Ctrl", "Left Shift"], settings.RewriteChips);
    }

    [Fact]
    public void RemovedTrigger_StaysOffAndShowsNoChips()
    {
        var settings = new HotkeySettings { TriggerRemoved = true };

        Assert.False(settings.IsTriggerReady);
        Assert.Empty(settings.TriggerVks);
        Assert.Empty(settings.TriggerChips);
    }

    [Fact]
    public void OldUntouchedOffTrigger_MovesToTheNewDefault()
    {
        // What every settings.json written before VED-70 holds for a user who never set a Trigger Key.
        var settings = JsonSerializer.Deserialize<HotkeySettings>(
            """{"HotkeyKey":"Ctrl","TriggerEnabled":false,"TriggerMode":"Single","TriggerKey":null}""")!;

        Assert.True(settings.IsTriggerReady);
        Assert.Equal([0xA2, 0x5B], settings.TriggerVks);
    }

    [Fact]
    public void ChosenTrigger_IsKeptEvenWhenItWasSwitchedOff()
    {
        // v3 had an on/off switch; a user who set F5 and turned it off keeps it off.
        var off = new HotkeySettings { TriggerEnabled = false, TriggerKey = "F5" };
        var on = new HotkeySettings { TriggerEnabled = true, TriggerKey = "F5" };

        Assert.False(off.IsTriggerReady);
        Assert.Equal(["F5"], on.TriggerChips);
        Assert.Equal([0x74], on.TriggerVks);
    }

    [Fact]
    public void CustomCombination_IsNotReplacedByTheDefault()
    {
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeCombination,
            ComboVks = [0xA3, 0x20],
            ComboLabels = ["Right Ctrl", "Space"],
        };

        Assert.Equal([0xA3, 0x20], settings.TriggerVks);
        Assert.Equal(["Right Ctrl", "Space"], settings.TriggerChips);
    }

    [Fact]
    public void TriggerRemoved_IsSavedAndReloaded()
    {
        string json = JsonSerializer.Serialize(new HotkeySettings { TriggerRemoved = true });

        Assert.False(JsonSerializer.Deserialize<HotkeySettings>(json)!.IsTriggerReady);
    }

    [Fact]
    public void SingleKeyTrigger_WithAKey_IsReady()
    {
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeSingle,
            TriggerKey = "F5",
        };

        Assert.True(settings.IsTriggerReady);
    }

    [Fact]
    public void ChordTrigger_WithBothHalves_IsReady()
    {
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeChord,
            ChordPrefix = "Left Ctrl",
            ChordSecondKeyVk = 0x20,
            ChordSecondKeyLabel = "Space",
        };

        Assert.True(settings.IsTriggerReady);
    }

    [Fact]
    public void TriggerEnabled_ButKeyNotYetChosen_UsesTheDefault()
    {
        var settings = new HotkeySettings { TriggerEnabled = true, TriggerMode = HotkeySettings.TriggerModeSingle };

        Assert.Equal([0xA2, 0x5B], settings.TriggerVks);
    }

    [Fact]
    public void ChordMode_MissingSecondKey_IsNotReady()
    {
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeChord,
            ChordPrefix = "Left Ctrl",
        };

        Assert.False(settings.IsTriggerReady);
    }

    [Fact]
    public void CombinationTrigger_WithOneToThreeKeys_IsReadyAndResolvesItsKeys()
    {
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeCombination,
            ComboVks = [0xA2, 0x5B],
            ComboLabels = ["Left Ctrl", "Left Win"],
        };

        Assert.True(settings.IsTriggerReady);
        Assert.Equal([0xA2, 0x5B], settings.TriggerVks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void CombinationTrigger_OutsideOneToThreeKeys_IsNotReady(int count)
    {
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeCombination,
            ComboVks = [.. Enumerable.Range(0x70, count)],
        };

        Assert.False(settings.IsTriggerReady);
    }

    [Fact]
    public void OlderSingleAndChordSettings_ResolveToTheSameKeysAsACombination()
    {
        var single = new HotkeySettings { TriggerEnabled = true, TriggerKey = "F5" };
        var chord = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeChord,
            ChordPrefix = "Left Ctrl",
            ChordSecondKeyVk = 0x20,
        };

        Assert.Equal(HotkeyKeyCatalog.ResolveVkCodes("F5"), single.TriggerVks);
        Assert.Equal([0xA2, 0x20], chord.TriggerVks);
    }

    [Fact]
    public void Rewrite_IsOnByDefaultWithLeftCtrlAndLeftShift()
    {
        var settings = new HotkeySettings();

        Assert.True(settings.RewriteEnabled);
        Assert.Equal([0xA2, 0xA0], settings.RewriteKeys);
        Assert.Equal(["Left Ctrl", "Left Shift"], settings.RewriteChips);
    }

    [Fact]
    public void Rewrite_UsesTheCapturedKeysWhenThereAre()
    {
        var settings = new HotkeySettings { RewriteVks = [0xA2, 0x20], RewriteLabels = ["Left Ctrl", "Space"] };

        Assert.Equal([0xA2, 0x20], settings.RewriteKeys);
        Assert.Equal(["Left Ctrl", "Space"], settings.RewriteChips);
    }

    [Fact]
    public void Rewrite_WhenRemoved_HasNoKeys()
    {
        var settings = new HotkeySettings { RewriteEnabled = false };

        Assert.Empty(settings.RewriteKeys);
        Assert.Empty(settings.RewriteChips);
    }

    [Fact]
    public void Rewrite_DerivedViewsAreNeverSerialized()
    {
        string json = JsonSerializer.Serialize(new HotkeySettings());

        Assert.DoesNotContain(nameof(HotkeySettings.RewriteKeys), json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(HotkeySettings.RewriteChips), json, StringComparison.Ordinal);
    }

    [Fact]
    public void IsTriggerReady_IsDerivedAndNeverSerialized()
    {
        string json = JsonSerializer.Serialize(new HotkeySettings { TriggerEnabled = true, TriggerKey = "F5" });

        Assert.DoesNotContain(nameof(HotkeySettings.IsTriggerReady), json, StringComparison.Ordinal);
    }
}
