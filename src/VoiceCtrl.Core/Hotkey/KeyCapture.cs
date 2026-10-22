using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>Which Settings row is recording a key.</summary>
public enum KeyCaptureTarget
{
    Hotkey,
    TriggerKey,
    PasteAgain,

    /// <summary>The hold that rewrites selected text by voice (ticket 39).</summary>
    RewriteKey,
}

public enum KeyCaptureStatus
{
    /// <summary>Keep listening; nothing to show.</summary>
    Waiting,

    /// <summary>That wasn't usable: show <see cref="KeyCaptureStep.Hint"/> and keep listening.</summary>
    Hint,

    /// <summary>Save <see cref="KeyCaptureStep.Result"/>.</summary>
    Done,

    /// <summary>Esc: stop listening and change nothing.</summary>
    Cancelled,
}

/// <param name="Result">With <see cref="KeyCaptureStatus.Done"/>: the dictation section with the new key in it.</param>
/// <param name="Warning">With Done: a known Windows behaviour the key may set off. Only ever a warning.</param>
public sealed record KeyCaptureStep(
    KeyCaptureStatus Status,
    string? Hint = null,
    DictationSection? Result = null,
    string? Warning = null);

/// <summary>
/// The "Press a key" step of the Hotkey, Trigger Key and Paste Again rows. The keyboard hook feeds
/// it every key while it listens (and swallows them, so nothing reaches another app); it decides
/// once every key is back up, so a combination is read whole and its releases never leak out.
/// <para>
/// The rules come from CONTEXT: the Hotkey is one key you double-tap and don't type with; the
/// Trigger Key is one such key or a held modifier plus any key, and never the Hotkey; Paste Again
/// is an ordinary shortcut. None of the three may be another's key.
/// </para>
/// </summary>
public sealed class KeyCapture(KeyCaptureTarget target, DictationSection current)
{
    public const string HotkeyNotAllowedHint = "Choose a key you don't type with, like Ctrl, Caps Lock or F8.";
    public const string HotkeyOneKeyHint = "Press just one key. You'll double-tap it.";
    public const string TriggerKeyHint = "Press one key like F8, or hold two or three keys together, like Ctrl + Win. Combinations need Ctrl, Alt, Shift or Win.";
    public const string PasteAgainHint = "Hold Ctrl, Alt or Win and press a key.";
    public const string WindowsOwnsHint = "Windows uses that combination itself. Choose another.";
    public const string IsHotkeyHint = "That's your Hotkey. Choose another key.";
    public const string IsTriggerKeyHint = "That's your Trigger Key. Choose another key.";
    public const string IsRewriteKeyHint = "That's your Rewrite key. Choose another key.";
    public const string IsPasteAgainHint = "That's your Paste Again key. Choose another key.";
    public const string IsPasteHint = "Ctrl + V is how VoiceCtrl pastes. Choose another combination.";

    private readonly HashSet<int> _down = [];

    /// <summary>Every key pressed since all keys were last up, in the order first pressed.</summary>
    private readonly List<int> _gesture = [];

    public KeyCaptureTarget Target { get; } = target;

    /// <summary>The Paste Again chord was fine but Windows refused it (another app registered it first).</summary>
    public static string TakenHint(PasteAgainKey key) =>
        $"Another app already uses {string.Join(" + ", key.Chips)}. Try another.";

    public KeyCaptureStep KeyDown(int vk)
    {
        if (vk == KeyNames.VkEscape)
        {
            return new KeyCaptureStep(KeyCaptureStatus.Cancelled);
        }

        // A held key auto-repeats its key-down; it is still one press.
        if (_down.Add(vk) && !_gesture.Contains(vk))
        {
            _gesture.Add(vk);
        }

        return new KeyCaptureStep(KeyCaptureStatus.Waiting);
    }

    public KeyCaptureStep KeyUp(int vk)
    {
        // A release without a press here began before listening did (the Enter that clicked "Change").
        if (!_down.Remove(vk) || _down.Count > 0)
        {
            return new KeyCaptureStep(KeyCaptureStatus.Waiting);
        }

        int[] gesture = [.. _gesture];
        _gesture.Clear();
        return Target switch
        {
            KeyCaptureTarget.Hotkey => ReadHotkey(gesture),
            KeyCaptureTarget.TriggerKey => ReadTriggerKey(gesture),
            KeyCaptureTarget.RewriteKey => ReadRewriteKey(gesture),
            _ => ReadPasteAgain(gesture),
        };
    }

    /// <summary>Forgets the keys so far, e.g. after Windows refused a Paste Again chord.</summary>
    public void Reset()
    {
        _down.Clear();
        _gesture.Clear();
    }

    private KeyCaptureStep ReadHotkey(int[] gesture)
    {
        if (gesture.Length != 1)
        {
            return Hint(HotkeyOneKeyHint);
        }

        string? name = KeyNames.CatalogName(gesture[0]);
        if (name is null)
        {
            return Hint(HotkeyNotAllowedHint);
        }

        // Either Ctrl is plain "Ctrl", as the default always was, so re-choosing Ctrl keeps both sides.
        if (name is "Left Ctrl" or "Right Ctrl")
        {
            name = HotkeyKeyCatalog.DefaultHotkeyKey;
        }

        HotkeySettings hotkeys = current.Hotkeys;
        if (hotkeys.IsTriggerReady && hotkeys.TriggerMode == HotkeySettings.TriggerModeSingle
            && hotkeys.TriggerKey is { } triggerKey && Overlaps(name, triggerKey))
        {
            return Hint(IsTriggerKeyHint);
        }

        return Done(current with { Hotkeys = hotkeys with { HotkeyKey = name } }, HotkeyKeyCatalog.WarningFor(name));
    }

    private KeyCaptureStep ReadTriggerKey(int[] gesture)
    {
        HotkeySettings hotkeys = current.Hotkeys;

        if (gesture.Length > 1 && HotkeyKeyCatalog.IsWindowsOwned(gesture))
        {
            return Hint(WindowsOwnsHint);
        }

        if (SameKeys(gesture, hotkeys.RewriteKeys))
        {
            return Hint(IsRewriteKeyHint);
        }

        if (gesture.Length == 1 && KeyNames.CatalogName(gesture[0]) is { } name)
        {
            if (Overlaps(name, hotkeys.HotkeyKey))
            {
                return Hint(IsHotkeyHint);
            }

            HotkeySettings single = hotkeys with
            {
                TriggerEnabled = true,
                TriggerRemoved = false,
                TriggerMode = HotkeySettings.TriggerModeSingle,
                TriggerKey = name,
                ChordPrefix = null,
                ChordSecondKeyVk = null,
                ChordSecondKeyLabel = null,
            };
            return Done(current with { Hotkeys = single }, HotkeyKeyCatalog.WarningFor(name));
        }

        if (gesture.Length == 2
            && KeyNames.CatalogName(gesture[0]) is { } prefix
            && HotkeyKeyCatalog.ChordPrefixes.Contains(prefix)
            && !KeyNames.IsModifier(gesture[1]))
        {
            int secondVk = gesture[1];
            PasteAgainKey pasteAgain = current.PasteAgain;
            if (pasteAgain.KeyVk == secondVk && pasteAgain.Modifiers.SequenceEqual([KeyNames.ModifierName(gesture[0])!]))
            {
                return Hint(IsPasteAgainHint);
            }

            string label = KeyNames.Label(secondVk);
            HotkeySettings chord = hotkeys with
            {
                TriggerEnabled = true,
                TriggerRemoved = false,
                TriggerMode = HotkeySettings.TriggerModeChord,
                TriggerKey = null,
                ChordPrefix = prefix,
                ChordSecondKeyVk = secondVk,
                ChordSecondKeyLabel = label,
            };
            return Done(current with { Hotkeys = chord }, HotkeyKeyCatalog.ChordWarningFor(prefix, label));
        }

        return ReadCombination(gesture);
    }

    /// <summary>One to three keys held together. Two or more must include a modifier, so a plain
    /// pair of letters can never become the Trigger Key, and at most one key may be a non-modifier.</summary>
    private KeyCaptureStep ReadCombination(int[] gesture)
    {
        bool shapeOk = gesture.Length is >= 2 and <= HotkeySettings.MaxComboKeys
            && gesture.Any(KeyNames.IsModifier)
            && gesture.Count(vk => !KeyNames.IsModifier(vk)) <= 1;
        if (!shapeOk)
        {
            return Hint(TriggerKeyHint);
        }

        if (HotkeyKeyCatalog.IsWindowsOwned(gesture))
        {
            return Hint(WindowsOwnsHint);
        }

        string[] labels = [.. gesture.Select(KeyNames.Label)];
        HotkeySettings combination = current.Hotkeys with
        {
            TriggerEnabled = true,
            TriggerRemoved = false,
            TriggerMode = HotkeySettings.TriggerModeCombination,
            TriggerKey = null,
            ChordPrefix = null,
            ChordSecondKeyVk = null,
            ChordSecondKeyLabel = null,
            ComboVks = gesture,
            ComboLabels = labels,
        };
        return Done(current with { Hotkeys = combination }, HotkeyKeyCatalog.CombinationWarningFor(gesture));
    }

    /// <summary>The Rewrite hold: one catalog key, or the same one-to-three key combinations the
    /// Trigger Key takes. Never the Hotkey, nor the same keys as the Trigger Key.</summary>
    private KeyCaptureStep ReadRewriteKey(int[] gesture)
    {
        HotkeySettings hotkeys = current.Hotkeys;
        string[] labels;
        string? warning;

        if (gesture.Length == 1)
        {
            if (KeyNames.CatalogName(gesture[0]) is not { } name)
            {
                return Hint(TriggerKeyHint);
            }

            if (Overlaps(name, hotkeys.HotkeyKey))
            {
                return Hint(IsHotkeyHint);
            }

            labels = [name];
            warning = HotkeyKeyCatalog.WarningFor(name);
        }
        else
        {
            bool shapeOk = gesture.Length is >= 2 and <= HotkeySettings.MaxComboKeys
                && gesture.Any(KeyNames.IsModifier)
                && gesture.Count(vk => !KeyNames.IsModifier(vk)) <= 1;
            if (!shapeOk)
            {
                return Hint(TriggerKeyHint);
            }

            if (HotkeyKeyCatalog.IsWindowsOwned(gesture))
            {
                return Hint(WindowsOwnsHint);
            }

            labels = [.. gesture.Select(KeyNames.Label)];
            warning = HotkeyKeyCatalog.CombinationWarningFor(gesture);
        }

        if (hotkeys.IsTriggerReady && SameKeys(gesture, hotkeys.TriggerVks))
        {
            return Hint(IsTriggerKeyHint);
        }

        HotkeySettings saved = hotkeys with { RewriteEnabled = true, RewriteVks = gesture, RewriteLabels = labels };
        return Done(current with { Hotkeys = saved }, warning);
    }

    private static bool SameKeys(IReadOnlyCollection<int> a, IReadOnlyCollection<int> b) =>
        a.Count > 0 && a.Count == b.Count && a.ToHashSet().SetEquals(b);

    private KeyCaptureStep ReadPasteAgain(int[] gesture)
    {
        int[] keys = [.. gesture.Where(vk => !KeyNames.IsModifier(vk))];
        if (keys.Length != 1)
        {
            return Hint(PasteAgainHint);
        }

        HashSet<string> held = [.. gesture.Select(KeyNames.ModifierName).OfType<string>()];
        var chord = new PasteAgainKey
        {
            Modifiers = [.. PasteAgainKey.ModifierOrder.Where(held.Contains)],
            KeyVk = keys[0],
            KeyLabel = KeyNames.Label(keys[0]),
        };
        if (chord.IsPasteShortcut)
        {
            return Hint(IsPasteHint);
        }

        if (!chord.IsValid)
        {
            return Hint(PasteAgainHint);
        }

        HotkeySettings hotkeys = current.Hotkeys;
        if (hotkeys.IsTriggerReady && hotkeys.TriggerMode == HotkeySettings.TriggerModeChord
            && hotkeys.ChordPrefix is not null && hotkeys.ChordSecondKeyVk == chord.KeyVk
            && chord.Modifiers.SequenceEqual([KeyNames.ModifierName(HotkeyKeyCatalog.ResolveVkCodes(hotkeys.ChordPrefix!)[0])!]))
        {
            return Hint(IsTriggerKeyHint);
        }

        return Done(current with { PasteAgain = chord }, warning: null);
    }

    /// <summary>Two catalog names share a physical key ("Ctrl" is both Left and Right Ctrl).</summary>
    private static bool Overlaps(string a, string b) =>
        HotkeyKeyCatalog.ResolveVkCodes(a).Intersect(HotkeyKeyCatalog.ResolveVkCodes(b)).Any();

    private static KeyCaptureStep Hint(string hint) => new(KeyCaptureStatus.Hint, hint);

    private static KeyCaptureStep Done(DictationSection result, string? warning) =>
        new(KeyCaptureStatus.Done, Result: result, Warning: warning);
}
