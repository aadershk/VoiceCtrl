using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Resolves a raw (vkCode, flags) pair from WH_KEYBOARD_LL to a tracked Ctrl key, or null if the
/// key isn't one we track. Most keyboards report the side-specific VK directly (the exact-match
/// branch, matching this app's original behavior unconditionally). Some Bluetooth/HID stacks
/// instead report the generic VK_CONTROL and rely on the extended-key flag to disambiguate sides
/// (the older WM_KEYDOWN convention). The fallback branch exists only for that case, so it can
/// never change behavior for a keyboard that already reports the side-specific code correctly.
/// </summary>
public static class TrackedCtrlKeyResolver
{
    public static int? Resolve(uint vkCode, uint flags)
    {
        if (vkCode == NativeMethods.VK_LCONTROL || vkCode == NativeMethods.VK_RCONTROL)
        {
            return (int)vkCode;
        }

        if (vkCode == NativeMethods.VK_CONTROL)
        {
            bool isExtended = (flags & NativeMethods.LLKHF_EXTENDED) != 0;
            return isExtended ? NativeMethods.VK_RCONTROL : NativeMethods.VK_LCONTROL;
        }

        return null;
    }
}
