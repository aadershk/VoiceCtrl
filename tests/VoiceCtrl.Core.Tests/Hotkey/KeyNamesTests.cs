using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class KeyNamesTests
{
    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    [InlineData(NativeMethods.VK_F5, "F5")]
    [InlineData(0x87, "F24")]
    [InlineData(0x20, "Space")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x21, "Page Up")]
    [InlineData(0x25, "Left")]
    [InlineData(0x60, "Num 0")]
    [InlineData(0xBA, ";")]
    [InlineData(0xBF, "/")]
    [InlineData(0xDC, "\\")]
    public void Label_NamesCommonKeysAsTheKeyCapSays(int vk, string expected)
    {
        Assert.Equal(expected, KeyNames.Label(vk));
    }

    [Theory]
    [InlineData(NativeMethods.VK_LCONTROL, "Left Ctrl")]
    [InlineData(NativeMethods.VK_RMENU, "Right Alt")]
    [InlineData(NativeMethods.VK_CAPITAL, "Caps Lock")]
    [InlineData(NativeMethods.VK_PAUSE, "Pause/Break")]
    public void Label_UsesTheCatalogNameForCatalogKeys(int vk, string expected)
    {
        Assert.Equal(expected, KeyNames.Label(vk));
    }

    [Fact]
    public void Label_UnknownKey_FallsBackToItsCode()
    {
        Assert.Equal("Key E8", KeyNames.Label(0xE8));
    }

    [Fact]
    public void CatalogName_IsTheReverseOfTheCatalog()
    {
        foreach (string key in HotkeyKeyCatalog.AllSingleKeys)
        {
            Assert.Equal(key, KeyNames.CatalogName(HotkeyKeyCatalog.ResolveVkCodes(key)[0]));
        }
    }

    [Fact]
    public void CatalogName_KeyOutsideTheCatalog_IsNull()
    {
        Assert.Null(KeyNames.CatalogName(0x41));
    }

    [Theory]
    [InlineData(NativeMethods.VK_LCONTROL, "Ctrl")]
    [InlineData(NativeMethods.VK_RSHIFT, "Shift")]
    [InlineData(NativeMethods.VK_LMENU, "Alt")]
    [InlineData(NativeMethods.VK_RWIN, "Win")]
    public void ModifierName_DropsTheSide(int vk, string expected)
    {
        Assert.Equal(expected, KeyNames.ModifierName(vk));
    }

    [Theory]
    [InlineData(NativeMethods.VK_CAPITAL)]
    [InlineData(0x41)]
    public void ModifierName_NonModifier_IsNull(int vk)
    {
        Assert.Null(KeyNames.ModifierName(vk));
    }
}
