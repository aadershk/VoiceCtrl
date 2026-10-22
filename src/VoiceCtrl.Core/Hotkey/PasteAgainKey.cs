using System.Text.Json.Serialization;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// The Paste Again key combination, kept in the settings store's dictation section. Unlike the
/// Hotkey and Trigger Key it is an ordinary shortcut, so the host registers it with Windows'
/// <c>RegisterHotKey</c>, which also tells us when another app already owns it (spec §12).
/// </summary>
public sealed record PasteAgainKey
{
    /// <summary>The order Windows writes shortcuts in ("Win + Shift + S", "Ctrl + Alt + Del").</summary>
    public static readonly IReadOnlyList<string> ModifierOrder = ["Win", "Ctrl", "Alt", "Shift"];

    /// <summary>Sideless modifier names, in <see cref="ModifierOrder"/>.</summary>
    public IReadOnlyList<string> Modifiers { get; init; } = ["Ctrl", "Shift"];

    /// <summary>The one key that isn't a modifier, as a VK code. Defaults to V: Ctrl+Shift+V was
    /// kept by the owner, knowing it takes over "paste without formatting" (DESIGN §13).</summary>
    public int KeyVk { get; init; } = 0x56;

    /// <summary>Captured with <see cref="KeyVk"/>, so the chip never re-derives it.</summary>
    public string KeyLabel { get; init; } = "V";

    /// <summary>One key chip per key, e.g. Ctrl, Shift, V.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Chips => [.. Modifiers, KeyLabel];

    /// <summary>The <c>RegisterHotKey</c> modifier bits (MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8).</summary>
    [JsonIgnore]
    public uint ModifierFlags => Modifiers.Aggregate(0u, (flags, name) => flags | name switch
    {
        "Alt" => 0x1u,
        "Ctrl" => 0x2u,
        "Shift" => 0x4u,
        "Win" => 0x8u,
        _ => 0u,
    });

    /// <summary>A chord Windows can register and that can't be typed by accident: at least one of
    /// Ctrl, Alt or Win (Shift and a key types a capital), only known modifiers, and a key. Never
    /// Ctrl+V itself: that is the paste VoiceCtrl sends, so it would set itself off.</summary>
    [JsonIgnore]
    public bool IsValid =>
        Modifiers is { Count: > 0 }
        && Modifiers.All(ModifierOrder.Contains)
        && Modifiers.Any(name => name != "Shift")
        && KeyVk is > 0 and < 0xFF
        && !string.IsNullOrEmpty(KeyLabel)
        && !IsPasteShortcut;

    /// <summary>Plain Ctrl+V.</summary>
    [JsonIgnore]
    public bool IsPasteShortcut => KeyVk == 0x56 && Modifiers is ["Ctrl"];

    public bool SameChord(PasteAgainKey other) =>
        KeyVk == other.KeyVk && ModifierFlags == other.ModifierFlags;
}
