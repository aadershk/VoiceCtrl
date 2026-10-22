using System.Text.Json.Serialization;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// The Hotkey and Trigger Key bindings, kept in the settings store's dictation section
/// (<see cref="VoiceCtrl.Core.Config.DictationSection.Hotkeys"/>). Immutable, so a binding read by
/// the keyboard hook can't change under it; an edit is a <c>with</c> copy saved through the store,
/// and the hook rebinds from the store's dictation change event (Settings apply live).
/// </summary>
public sealed record HotkeySettings
{
    public string HotkeyKey { get; init; } = HotkeyKeyCatalog.DefaultHotkeyKey;

    /// <summary>Whether a Trigger Key the user chose is switched on. Only meaningful once a key is
    /// stored (v3 had an on/off switch); with none stored the default applies, see
    /// <see cref="IsTriggerReady"/>.</summary>
    public bool TriggerEnabled { get; init; }

    /// <summary>True once the user removed the Trigger Key (the row's Remove), so the default
    /// Left Ctrl + Left Win no longer applies. Settings written before the default existed carry
    /// the same shape for "never set" and "removed", and read as never set.</summary>
    public bool TriggerRemoved { get; init; }

    public string TriggerMode { get; init; } = TriggerModeSingle;

    /// <summary>Set only in single-key mode.</summary>
    public string? TriggerKey { get; init; }

    /// <summary>Set only in chord mode: the held modifier prefix.</summary>
    public string? ChordPrefix { get; init; }

    /// <summary>Set only in chord mode: the chord's open-ended second key, as the raw VK code
    /// captured live — there is no fixed table for this half of a chord to look it up in.</summary>
    public int? ChordSecondKeyVk { get; init; }

    /// <summary>Set only in chord mode: a friendly display label for <see cref="ChordSecondKeyVk"/>
    /// (e.g. "Space", "F5", "A"), captured once alongside it so the Settings screen never has to
    /// re-derive one from a raw VK code on every load.</summary>
    public string? ChordSecondKeyLabel { get; init; }

    /// <summary>Set only in combination mode: the one to three keys held together, as VK codes
    /// captured live. Every new capture is saved this way; Single and Chord stay readable.</summary>
    public IReadOnlyList<int>? ComboVks { get; init; }

    /// <summary>Set only in combination mode: the display label of each of <see cref="ComboVks"/>,
    /// in the same order (the Settings chips).</summary>
    public IReadOnlyList<string>? ComboLabels { get; init; }

    /// <summary>False once the user removed the Rewrite key (the row's Remove). On by default.</summary>
    public bool RewriteEnabled { get; init; } = true;

    /// <summary>The keys held to rewrite selected text by voice, as VK codes captured live. Null
    /// until the user chooses: the default, Left Ctrl + Left Shift, then applies.</summary>
    public IReadOnlyList<int>? RewriteVks { get; init; }

    /// <summary>The display label of each of <see cref="RewriteVks"/>, in the same order.</summary>
    public IReadOnlyList<string>? RewriteLabels { get; init; }

    public static readonly IReadOnlyList<int> DefaultRewriteVks = [Interop.NativeMethods.VK_LCONTROL, Interop.NativeMethods.VK_LSHIFT];

    /// <summary>The Trigger Key until the user chooses another or removes it: hold Left Ctrl +
    /// Left Win to talk (owner decision, 2026-10-17).</summary>
    public static readonly IReadOnlyList<int> DefaultTriggerVks = [Interop.NativeMethods.VK_LCONTROL, Interop.NativeMethods.VK_LWIN];

    public const string TriggerModeSingle = "Single";
    public const string TriggerModeChord = "Chord";
    public const string TriggerModeCombination = "Combination";

    public const int MaxComboKeys = 3;

    /// <summary>Whether the user stored any Trigger Key of their own, complete or not. When none,
    /// the default applies unless the user removed it.</summary>
    [JsonIgnore]
    private bool HasChosenTrigger => ComboVks is not null || TriggerKey is not null || ChordPrefix is not null || ChordSecondKeyVk is not null;

    /// <summary>Whether the Trigger Key has everything it needs to actually engage: the default
    /// (nothing chosen, not removed), or an enabled single key, complete chord (prefix and second
    /// key both set) or one to three combination keys. Read by the app at startup to decide
    /// whether to build a <see cref="TriggerKeyTracker"/> at all.</summary>
    [JsonIgnore]
    public bool IsTriggerReady =>
        !TriggerRemoved && (!HasChosenTrigger || TriggerEnabled && TriggerMode switch
        {
            TriggerModeCombination => ComboVks is { Count: >= 1 and <= MaxComboKeys },
            TriggerModeChord => ChordPrefix is not null && ChordSecondKeyVk is not null,
            _ => TriggerKey is not null,
        });

    /// <summary>The keys the Rewrite hold needs: the captured ones, else Left Ctrl + Left Shift;
    /// empty once removed. The keyboard hook tracks these like <see cref="TriggerVks"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<int> RewriteKeys =>
        !RewriteEnabled ? [] : RewriteVks is { Count: >= 1 and <= MaxComboKeys } ? RewriteVks : DefaultRewriteVks;

    /// <summary>One chip label per key of <see cref="RewriteKeys"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> RewriteChips =>
        !RewriteEnabled ? []
        : RewriteVks is { Count: >= 1 and <= MaxComboKeys } && RewriteLabels is { } labels && labels.Count == RewriteVks.Count ? labels
        : [.. RewriteKeys.Select(KeyNames.Label)];

    /// <summary>The keys the Trigger Key needs held, whichever mode saved them; empty when not ready.
    /// This is what the keyboard hook tracks, so older Single and Chord settings keep working.</summary>
    [JsonIgnore]
    public IReadOnlyList<int> TriggerVks
    {
        get
        {
            if (!IsTriggerReady)
            {
                return [];
            }

            if (!HasChosenTrigger)
            {
                return DefaultTriggerVks;
            }

            return TriggerMode switch
            {
                TriggerModeCombination => ComboVks!,
                TriggerModeChord => [HotkeyKeyCatalog.ResolveVkCodes(ChordPrefix!)[0], ChordSecondKeyVk!.Value],
                _ => [HotkeyKeyCatalog.ResolveVkCodes(TriggerKey!)[0]],
            };
        }
    }

    /// <summary>One chip label per Trigger Key key (Settings, Setup and the status hint); empty
    /// when the Trigger Key is off.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> TriggerChips
    {
        get
        {
            if (!IsTriggerReady)
            {
                return [];
            }

            if (!HasChosenTrigger)
            {
                return [.. DefaultTriggerVks.Select(KeyNames.Label)];
            }

            return TriggerMode switch
            {
                TriggerModeCombination => ComboLabels is { } labels && labels.Count == ComboVks!.Count ? labels : [.. ComboVks!.Select(KeyNames.Label)],
                TriggerModeChord => [ChordPrefix!, ChordSecondKeyLabel ?? KeyNames.Label(ChordSecondKeyVk!.Value)],
                _ => [TriggerKey!],
            };
        }
    }
}
