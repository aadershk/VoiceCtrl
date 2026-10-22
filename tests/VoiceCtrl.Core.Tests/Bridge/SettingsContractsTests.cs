using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;

namespace VoiceCtrl.Core.Tests.Bridge;

public class SettingsContractsTests
{
    [Fact]
    public void DictationView_Defaults_ShowCtrlLeftCtrlLeftWinAndCtrlShiftV()
    {
        DictationSettingsView view = DictationSettingsView.From(new DictationSection(), pasteAgainTaken: false);

        Assert.Null(view.MicrophoneId);
        Assert.Equal(["Ctrl"], view.Hotkey);
        Assert.Equal(["Left Ctrl", "Left Win"], view.TriggerKey);
        Assert.Equal(["Ctrl", "Shift", "V"], view.PasteAgain);
    }

    [Fact]
    public void DictationView_ChordTriggerKey_IsItsTwoChips()
    {
        var section = new DictationSection
        {
            Hotkeys = new HotkeySettings
            {
                TriggerEnabled = true,
                TriggerMode = HotkeySettings.TriggerModeChord,
                ChordPrefix = "Left Alt",
                ChordSecondKeyVk = 0x20,
                ChordSecondKeyLabel = "Space",
            },
        };

        Assert.Equal(["Left Alt", "Space"], DictationSettingsView.From(section, false).TriggerKey);
    }

    [Fact]
    public void DictationView_FreshSettings_ShowTheTwoDefaults()
    {
        DictationSettingsView view = DictationSettingsView.From(new DictationSection(), false);

        Assert.Equal(["Left Ctrl", "Left Win"], view.TriggerKey);
        Assert.Equal(["Left Ctrl", "Left Shift"], view.RewriteKey);
    }

    [Fact]
    public void DictationView_FlagsTheRewriteDefaultOnlyWhenSeveralInputLanguagesAreInstalled()
    {
        var section = new DictationSection();

        DictationSettingsView several = DictationSettingsView.From(section, false, multipleInputLanguages: true);
        DictationSettingsView one = DictationSettingsView.From(section, false, multipleInputLanguages: false);

        Assert.Contains("keyboard language", several.RewriteKeyConflict);
        Assert.Null(several.TriggerKeyConflict);
        Assert.Null(one.RewriteKeyConflict);
    }

    [Fact]
    public void DictationView_RemovedTriggerKey_ShowsAsNotSet()
    {
        var section = new DictationSection { Hotkeys = new HotkeySettings { TriggerRemoved = true } };

        Assert.Null(DictationSettingsView.From(section, false).TriggerKey);
    }

    [Fact]
    public void KeyCaptureTarget_TravelsAsCamelCase()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new CaptureKey(KeyCaptureTarget.PasteAgain), BridgeJsonContext.Default.CaptureKey);

        Assert.Equal("""{"target":"pasteAgain"}""", json);
    }
}
