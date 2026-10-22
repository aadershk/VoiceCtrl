using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// Hold keys that can clash with something Windows does itself (VED-74). VoiceCtrl now starts
/// recording the moment the keys go down, so the user is told, where they pick the keys, when their
/// choice is also a Windows shortcut. Only ever a notice: the keys are saved either way. Unlike
/// <see cref="HotkeyKeyCatalog"/>'s fixed warnings, this one depends on the PC (the input-language
/// switch only exists with two or more layouts installed), so the host passes that in.
/// </summary>
public static class HotkeyConflicts
{
    /// <summary>The warning for <paramref name="vks"/> (one hold key set), or null when nothing is
    /// known to clash.</summary>
    /// <param name="multipleInputLanguages">Two or more keyboard layouts are installed, so Windows'
    /// Ctrl + Shift and Alt + Shift really switch the language.</param>
    public static string? For(IReadOnlyList<int> vks, bool multipleInputLanguages)
    {
        if (vks.Count == 0)
        {
            return null;
        }

        string keys = string.Join(" + ", vks.Select(KeyNames.Label));

        if (multipleInputLanguages && vks.Count == 2 && IsLayoutSwitch(vks))
        {
            return $"{keys} also switches your keyboard language. Turn that off in Windows settings or pick other keys.";
        }

        bool hasWin = vks.Any(vk => vk is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN);
        if (!hasWin)
        {
            return null;
        }

        if (vks.Count == 1)
        {
            return $"{keys} on its own opens the Start menu. Pick keys with another key beside it if the menu still appears.";
        }

        return vks.Any(vk => !KeyNames.IsModifier(vk))
            ? $"{keys} may also run a Windows shortcut. Pick other keys if something else opens."
            : null;
    }

    private static bool IsLayoutSwitch(IReadOnlyList<int> vks)
    {
        bool shift = vks.Any(vk => vk is NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT);
        bool ctrlOrAlt = vks.Any(vk => vk is NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL
            or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU);
        return shift && ctrlOrAlt;
    }
}
