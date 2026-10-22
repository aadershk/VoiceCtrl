using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Tests.Hotkey;

/// <summary>Plain-language warnings for hold keys that can clash with a Windows shortcut (VED-74).
/// Always a notice, never a block.</summary>
public class HotkeyConflictsTests
{
    private const int Space = 0x20;
    private const int KeyE = 0x45;

    [Fact]
    public void CtrlShift_WithSeveralLayouts_WarnsItSwitchesTheKeyboardLanguage()
    {
        string? warning = HotkeyConflicts.For([NativeMethods.VK_LCONTROL, NativeMethods.VK_LSHIFT], multipleInputLanguages: true);

        Assert.Equal(
            "Left Ctrl + Left Shift also switches your keyboard language. Turn that off in Windows settings or pick other keys.",
            warning);
    }

    [Fact]
    public void CtrlShift_WithOneLayout_DoesNotWarn() =>
        Assert.Null(HotkeyConflicts.For([NativeMethods.VK_LCONTROL, NativeMethods.VK_LSHIFT], multipleInputLanguages: false));

    [Fact]
    public void AltShift_WithSeveralLayouts_Warns_RightKeysToo()
    {
        string? warning = HotkeyConflicts.For([NativeMethods.VK_RMENU, NativeMethods.VK_RSHIFT], multipleInputLanguages: true);

        Assert.NotNull(warning);
        Assert.Contains("Right Alt + Right Shift", warning);
    }

    [Fact]
    public void TheDefaultTalkKeys_CtrlWin_DoNotWarn() =>
        Assert.Null(HotkeyConflicts.For(HotkeySettings.DefaultTriggerVks, multipleInputLanguages: true));

    [Fact]
    public void AWinKeyAlone_WarnsAboutTheStartMenu()
    {
        string? warning = HotkeyConflicts.For([NativeMethods.VK_LWIN], multipleInputLanguages: false);

        Assert.NotNull(warning);
        Assert.Contains("Start menu", warning);
    }

    [Fact]
    public void WinWithATypedKey_WarnsItMayRunAWindowsShortcut()
    {
        string? warning = HotkeyConflicts.For([NativeMethods.VK_LWIN, KeyE], multipleInputLanguages: false);

        Assert.NotNull(warning);
        Assert.Contains("Left Win + E", warning);
    }

    [Fact]
    public void CtrlSpace_AndOtherKeys_DoNotWarnHere() =>
        Assert.Null(HotkeyConflicts.For([NativeMethods.VK_LCONTROL, Space], multipleInputLanguages: true));

    [Fact]
    public void ThreeKeysWithShift_DoNotWarn() =>
        Assert.Null(HotkeyConflicts.For([NativeMethods.VK_LCONTROL, NativeMethods.VK_LSHIFT, KeyE], multipleInputLanguages: true));

    [Fact]
    public void NoKeys_DoNotWarn() =>
        Assert.Null(HotkeyConflicts.For([], multipleInputLanguages: true));
}
