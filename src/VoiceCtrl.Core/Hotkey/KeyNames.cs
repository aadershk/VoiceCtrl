using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// What a key is called on screen: the <see cref="HotkeyKeyCatalog"/> name for catalog keys, the
/// key-cap text for everything else a chord can end in. VK codes arrive already passed through
/// <see cref="PhysicalKeyResolver.Normalize"/>, so Ctrl, Shift and Alt are always side-specific.
/// Punctuation uses the US key caps: the label is captured once, when the key is chosen, and only
/// ever shown, never matched.
/// </summary>
public static class KeyNames
{
    public const int VkEscape = 0x1B;

    private static readonly Dictionary<int, string> CatalogNames =
        HotkeyKeyCatalog.AllSingleKeys.ToDictionary(key => HotkeyKeyCatalog.ResolveVkCodes(key)[0]);

    private static readonly Dictionary<int, string> Modifiers = new()
    {
        [NativeMethods.VK_LCONTROL] = "Ctrl",
        [NativeMethods.VK_RCONTROL] = "Ctrl",
        [NativeMethods.VK_LSHIFT] = "Shift",
        [NativeMethods.VK_RSHIFT] = "Shift",
        [NativeMethods.VK_LMENU] = "Alt",
        [NativeMethods.VK_RMENU] = "Alt",
        [NativeMethods.VK_LWIN] = "Win",
        [NativeMethods.VK_RWIN] = "Win",
    };

    private static readonly Dictionary<int, string> Named = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [VkEscape] = "Esc",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "Print Screen",
        [0x2E] = "Delete",
        [0x6A] = "Num *",
        [0x6B] = "Num +",
        [0x6D] = "Num -",
        [0x6E] = "Num .",
        [0x6F] = "Num /",
        [0x90] = "Num Lock",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
    };

    /// <summary>The catalog name ("Left Ctrl", "F8"), or null for a key the Hotkey can't be.</summary>
    public static string? CatalogName(int vk) => CatalogNames.GetValueOrDefault(vk);

    /// <summary>"Ctrl", "Shift", "Alt" or "Win" for either side of a modifier; null otherwise.</summary>
    public static string? ModifierName(int vk) => Modifiers.GetValueOrDefault(vk);

    public static bool IsModifier(int vk) => Modifiers.ContainsKey(vk);

    /// <summary>The label a key chip shows for <paramref name="vk"/>.</summary>
    public static string Label(int vk)
    {
        if (CatalogNames.TryGetValue(vk, out string? catalogName))
        {
            return catalogName;
        }

        if (Named.TryGetValue(vk, out string? name))
        {
            return name;
        }

        return vk switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
            _ => $"Key {vk:X2}",
        };
    }
}
