using VoiceCtrl.Core.Hotkey;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class PasteAgainKeyTests
{
    [Fact]
    public void Default_IsCtrlShiftV()
    {
        // DESIGN §13: the owner kept Ctrl+Shift+V (2026-09-28).
        var key = new PasteAgainKey();

        Assert.Equal(["Ctrl", "Shift", "V"], key.Chips);
        Assert.Equal(0x56, key.KeyVk);
    }

    [Fact]
    public void ModifierFlags_MapToRegisterHotKeysBits()
    {
        var key = new PasteAgainKey { Modifiers = ["Win", "Ctrl", "Alt", "Shift"], KeyVk = 0x42, KeyLabel = "B" };

        // MOD_ALT 1 | MOD_CONTROL 2 | MOD_SHIFT 4 | MOD_WIN 8.
        Assert.Equal(0xFu, key.ModifierFlags);
    }

    [Fact]
    public void IsValid_NeedsAModifierOtherThanShiftAndAKey()
    {
        Assert.True(new PasteAgainKey().IsValid);
        Assert.False(new PasteAgainKey { Modifiers = [] }.IsValid);
        Assert.False(new PasteAgainKey { Modifiers = ["Shift"] }.IsValid);
        Assert.False(new PasteAgainKey { Modifiers = ["Ctrl", "Hyper"] }.IsValid);
        Assert.False(new PasteAgainKey { KeyVk = 0 }.IsValid);
        Assert.False(new PasteAgainKey { Modifiers = ["Ctrl"] }.IsValid);
    }

    [Fact]
    public void SameChord_ComparesTheChordNotTheListInstances()
    {
        Assert.True(new PasteAgainKey().SameChord(new PasteAgainKey { Modifiers = ["Ctrl", "Shift"] }));
        Assert.False(new PasteAgainKey().SameChord(new PasteAgainKey { Modifiers = ["Ctrl", "Alt"] }));
    }
}
