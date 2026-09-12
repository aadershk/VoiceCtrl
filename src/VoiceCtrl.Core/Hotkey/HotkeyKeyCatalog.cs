using VoiceCtrl.Core.Interop;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// The fixed set of keys a user can assign to the Hotkey or a single-key Trigger Key, plus the
/// smaller set assignable as a chord's held prefix, per the widened key set locked in on
/// .scratch/hub-ui-refresh/issues/03-design-hotkey-editor.md's Addendum. All modifier-class or
/// otherwise safe-alone (nobody presses these mid-sentence while dictating), same reasoning as the
/// original 9-key allowlist, just wider.
/// </summary>
public static class HotkeyKeyCatalog
{
    /// <summary>Hotkey's shipped-default value: today's unconfigured behavior (double-tap of either
    /// Left or Right Ctrl). Deliberately not a member of any picker list below — reachable only as
    /// the value nobody has changed away from yet, matching the locked prototype
    /// (.scratch/hub-ui-refresh/prototypes/14c-settings-chords.html)'s `hotkeyKey = 'Ctrl'` default.
    /// </summary>
    public const string DefaultHotkeyKey = "Ctrl";

    public sealed record KeyGroup(string Label, IReadOnlyList<string> Keys);

    public static IReadOnlyList<KeyGroup> SingleKeyGroups { get; } =
    [
        new("Modifiers", ["Left Ctrl", "Right Ctrl", "Left Shift", "Right Shift", "Left Alt", "Right Alt", "Left Win", "Right Win", "Caps Lock"]),
        new("Function", ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"]),
        new("Utility", ["Menu", "Scroll Lock", "Pause/Break", "Insert"]),
    ];

    public static IReadOnlyList<string> AllSingleKeys { get; } =
        SingleKeyGroups.SelectMany(g => g.Keys).ToList();

    /// <summary>Left/Right Ctrl, Shift, Alt, Win — the only keys assignable as a chord's held
    /// prefix. Caps Lock is excluded: unlike these, it isn't used as a held modifier.</summary>
    public static IReadOnlyList<string> ChordPrefixes { get; } =
        ["Left Ctrl", "Right Ctrl", "Left Shift", "Right Shift", "Left Alt", "Right Alt", "Left Win", "Right Win"];

    /// <summary>Curated, known Windows-reserved behaviors for a single key pressed alone. Not a
    /// live "is anything else using this" scan — Windows exposes no such lookup (see ticket 03) —
    /// so this only covers behaviors known ahead of time, and only ever warns-and-suggests, never
    /// blocks the choice.</summary>
    public static IReadOnlyDictionary<string, string> KeyWarnings { get; } = new Dictionary<string, string>
    {
        ["Left Shift"] = "May trigger Windows' Sticky Keys prompt.",
        ["Right Shift"] = "May trigger Windows' Sticky Keys prompt.",
        ["Left Win"] = "Alone, Windows treats this as the Start Menu key.",
        ["Right Win"] = "Alone, Windows treats this as the Start Menu key.",
        ["Left Alt"] = "Alone, Windows treats this as menu-focus (Alt key) toggle.",
        ["Right Alt"] = "Alone, Windows treats this as menu-focus (Alt key) toggle.",
    };

    /// <summary>Curated reserved-shortcut chords, keyed "{Prefix}+{SecondKeyLabel}". The second key
    /// is open-ended (captured live, not chosen from a list), so this can only ever cover
    /// combinations known ahead of time, same non-exhaustive stance as <see cref="KeyWarnings"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ChordWarnings { get; } = new Dictionary<string, string>
    {
        ["Left Ctrl+Space"] = "Toggles the input method on some Windows setups.",
        ["Right Ctrl+Space"] = "Toggles the input method on some Windows setups.",
    };

    public static string? WarningFor(string key) => KeyWarnings.GetValueOrDefault(key);

    public static string? ChordWarningFor(string prefix, string secondKeyLabel) =>
        ChordWarnings.GetValueOrDefault($"{prefix}+{secondKeyLabel}");

    /// <summary>
    /// The VK code(s) that count as <paramref name="keyName"/> once every raw hook keystroke has
    /// passed through <see cref="PhysicalKeyResolver.Normalize"/> (so a Bluetooth keyboard's
    /// generic Ctrl/Shift/Alt-plus-extended-flag reports already land on the correct side). The
    /// default sentinel <see cref="DefaultHotkeyKey"/> resolves to both Left and Right Ctrl,
    /// matching today's unconfigured behavior; every other name resolves to exactly one VK code.
    /// Empty for a name this catalog does not recognize.
    /// </summary>
    public static IReadOnlyList<int> ResolveVkCodes(string keyName) => keyName switch
    {
        DefaultHotkeyKey => [NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL],
        "Left Ctrl" => [NativeMethods.VK_LCONTROL],
        "Right Ctrl" => [NativeMethods.VK_RCONTROL],
        "Left Shift" => [NativeMethods.VK_LSHIFT],
        "Right Shift" => [NativeMethods.VK_RSHIFT],
        "Left Alt" => [NativeMethods.VK_LMENU],
        "Right Alt" => [NativeMethods.VK_RMENU],
        "Left Win" => [NativeMethods.VK_LWIN],
        "Right Win" => [NativeMethods.VK_RWIN],
        "Caps Lock" => [NativeMethods.VK_CAPITAL],
        "F1" => [NativeMethods.VK_F1],
        "F2" => [NativeMethods.VK_F2],
        "F3" => [NativeMethods.VK_F3],
        "F4" => [NativeMethods.VK_F4],
        "F5" => [NativeMethods.VK_F5],
        "F6" => [NativeMethods.VK_F6],
        "F7" => [NativeMethods.VK_F7],
        "F8" => [NativeMethods.VK_F8],
        "F9" => [NativeMethods.VK_F9],
        "F10" => [NativeMethods.VK_F10],
        "F11" => [NativeMethods.VK_F11],
        "F12" => [NativeMethods.VK_F12],
        "Menu" => [NativeMethods.VK_APPS],
        "Scroll Lock" => [NativeMethods.VK_SCROLL],
        "Pause/Break" => [NativeMethods.VK_PAUSE],
        "Insert" => [NativeMethods.VK_INSERT],
        _ => [],
    };
}
