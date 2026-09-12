using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class HotkeyKeyCatalogTests
{
    [Fact]
    public void DefaultHotkeyKey_ResolvesToBothCtrlSides()
    {
        IReadOnlyList<int> vkCodes = HotkeyKeyCatalog.ResolveVkCodes(HotkeyKeyCatalog.DefaultHotkeyKey);

        Assert.Equal([NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL], vkCodes);
    }

    [Theory]
    [InlineData("Left Ctrl", NativeMethods.VK_LCONTROL)]
    [InlineData("Right Ctrl", NativeMethods.VK_RCONTROL)]
    [InlineData("Left Shift", NativeMethods.VK_LSHIFT)]
    [InlineData("Right Shift", NativeMethods.VK_RSHIFT)]
    [InlineData("Left Alt", NativeMethods.VK_LMENU)]
    [InlineData("Right Alt", NativeMethods.VK_RMENU)]
    [InlineData("Left Win", NativeMethods.VK_LWIN)]
    [InlineData("Right Win", NativeMethods.VK_RWIN)]
    [InlineData("Caps Lock", NativeMethods.VK_CAPITAL)]
    [InlineData("F5", NativeMethods.VK_F5)]
    [InlineData("F12", NativeMethods.VK_F12)]
    [InlineData("Menu", NativeMethods.VK_APPS)]
    [InlineData("Scroll Lock", NativeMethods.VK_SCROLL)]
    [InlineData("Pause/Break", NativeMethods.VK_PAUSE)]
    [InlineData("Insert", NativeMethods.VK_INSERT)]
    public void EachCatalogKey_ResolvesToExactlyOneDistinctVkCode(string keyName, int expectedVk)
    {
        IReadOnlyList<int> vkCodes = HotkeyKeyCatalog.ResolveVkCodes(keyName);

        Assert.Equal([expectedVk], vkCodes);
    }

    [Fact]
    public void UnknownKeyName_ResolvesToEmpty()
    {
        Assert.Empty(HotkeyKeyCatalog.ResolveVkCodes("Not A Real Key"));
    }

    [Fact]
    public void EveryCatalogKey_ResolvesToADistinctVkCode()
    {
        // Every entry in every group must be independently trackable: two names that silently
        // resolved to the same VK would make picking one a hidden alias for the other.
        var seen = new HashSet<int>();
        foreach (string key in HotkeyKeyCatalog.AllSingleKeys)
        {
            IReadOnlyList<int> vkCodes = HotkeyKeyCatalog.ResolveVkCodes(key);
            Assert.Single(vkCodes);
            Assert.True(seen.Add(vkCodes[0]), $"{key} resolved to a VK code already used by another key.");
        }
    }

    [Fact]
    public void ChordPrefixes_ExcludeCapsLock()
    {
        Assert.DoesNotContain("Caps Lock", HotkeyKeyCatalog.ChordPrefixes);
    }

    [Fact]
    public void ChordPrefixes_AreAllSidedModifiers()
    {
        Assert.Equal(8, HotkeyKeyCatalog.ChordPrefixes.Count);
        foreach (string prefix in HotkeyKeyCatalog.ChordPrefixes)
        {
            Assert.Contains(HotkeyKeyCatalog.AllSingleKeys, key => key == prefix);
        }
    }

    [Fact]
    public void WarningFor_UnwarnedKey_IsNull()
    {
        Assert.Null(HotkeyKeyCatalog.WarningFor("F5"));
    }

    [Fact]
    public void WarningFor_KnownReservedKey_IsNotNull()
    {
        Assert.NotNull(HotkeyKeyCatalog.WarningFor("Left Win"));
    }

    [Fact]
    public void ChordWarningFor_KnownReservedCombo_IsNotNull()
    {
        Assert.NotNull(HotkeyKeyCatalog.ChordWarningFor("Left Ctrl", "Space"));
    }

    [Fact]
    public void ChordWarningFor_UnknownCombo_IsNull()
    {
        Assert.Null(HotkeyKeyCatalog.ChordWarningFor("Left Alt", "Q"));
    }
}
