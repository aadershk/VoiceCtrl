using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Normalizes a raw (vkCode, flags) pair from WH_KEYBOARD_LL into the VK code callers should treat
/// it as. Most keyboards report the side-specific VK directly for Ctrl/Shift/Alt (the exact-match
/// case, falling straight through below). Some Bluetooth/HID stacks instead report the generic
/// VK_CONTROL/VK_SHIFT/VK_MENU and rely on the extended-key flag to disambiguate sides (the older
/// WM_KEYDOWN convention) — resolved the same way for all three, not just Ctrl. This widens what
/// was VoiceCtrl.Core's Ctrl-only resolver (TrackedCtrlKeyResolver) into the general form the
/// Hotkey/Trigger Key editor needs now that either can be assigned to any key in
/// <see cref="HotkeyKeyCatalog"/>, or a chord.
///
/// Every other vkCode (letters, digits, F-keys, Caps Lock, a chord's open-ended second key, ...)
/// passes through unchanged rather than resolving to null: there is no side ambiguity to resolve
/// for them, and a chord's second key in particular has to survive this call intact since it isn't
/// drawn from a fixed table at all.
/// </summary>
public static class PhysicalKeyResolver
{
    private static readonly (int Left, int Right, int Generic)[] SidedPairs =
    [
        (NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL, NativeMethods.VK_CONTROL),
        (NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT, NativeMethods.VK_SHIFT),
        (NativeMethods.VK_LMENU, NativeMethods.VK_RMENU, NativeMethods.VK_MENU),
    ];

    public static int Normalize(uint vkCode, uint flags)
    {
        foreach ((int left, int right, int generic) in SidedPairs)
        {
            if (vkCode == (uint)generic)
            {
                bool isExtended = (flags & NativeMethods.LLKHF_EXTENDED) != 0;
                return isExtended ? right : left;
            }
        }

        return (int)vkCode;
    }
}
