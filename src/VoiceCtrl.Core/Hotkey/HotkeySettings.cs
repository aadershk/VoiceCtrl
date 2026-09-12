using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Hotkey;

/// <summary>
/// The Hotkey and Trigger Key bindings, persisted to %LocalAppData%\VoiceCtrl\hotkey.json and
/// edited only through the Hub's Settings screen — never hand-edited, unlike dictionary.txt/
/// snippets.txt/profiles.json, so this stays a flat DTO rather than needing those files' header-
/// preservation machinery. Applies on the next restart (ticket 03's answer): the keyboard hook and
/// its tracked keys are built once at startup from whatever this holds at that moment.
/// </summary>
public sealed class HotkeySettings
{
    public string HotkeyKey { get; set; } = HotkeyKeyCatalog.DefaultHotkeyKey;

    public bool TriggerEnabled { get; set; }

    public string TriggerMode { get; set; } = TriggerModeSingle;

    /// <summary>Set only in single-key mode.</summary>
    public string? TriggerKey { get; set; }

    /// <summary>Set only in chord mode: the held modifier prefix.</summary>
    public string? ChordPrefix { get; set; }

    /// <summary>Set only in chord mode: the chord's open-ended second key, as the raw VK code
    /// captured live — there is no fixed table for this half of a chord to look it up in.</summary>
    public int? ChordSecondKeyVk { get; set; }

    /// <summary>Set only in chord mode: a friendly display label for <see cref="ChordSecondKeyVk"/>
    /// (e.g. "Space", "F5", "A"), captured once alongside it so the Settings screen never has to
    /// re-derive one from a raw VK code on every load.</summary>
    public string? ChordSecondKeyLabel { get; set; }

    public const string TriggerModeSingle = "Single";
    public const string TriggerModeChord = "Chord";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Whether the Trigger Key is enabled and has everything it needs to actually engage:
    /// a single key, or a complete chord (prefix and second key both set). Read by the app at
    /// startup to decide whether to build a <see cref="TriggerKeyTracker"/> at all.</summary>
    public bool IsTriggerReady =>
        TriggerEnabled && (TriggerMode == TriggerModeChord
            ? ChordPrefix is not null && ChordSecondKeyVk is not null
            : TriggerKey is not null);

    public static HotkeySettings Load() => Load(UserDataPaths.Hotkey);

    /// <summary>Never throws. A missing or malformed file is treated as the shipped default
    /// (today's unconfigured Ctrl double-tap, Trigger Key off) — the same never-refuse-to-start
    /// stance as every other user file this app reads.</summary>
    internal static HotkeySettings Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new HotkeySettings();
        }

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<HotkeySettings>(json, ReadOptions) ?? new HotkeySettings();
        }
        catch (JsonException ex)
        {
            SimpleFileLogger.LogInfo($"hotkey.json is not valid JSON, using defaults: {ex.Message}");
            return new HotkeySettings();
        }
        catch (IOException)
        {
            return new HotkeySettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new HotkeySettings();
        }
    }

    public void Save() => Save(UserDataPaths.Hotkey);

    /// <summary>Best-effort, same reasoning as TranscriptionModeStore.Save: a failed write to
    /// LocalAppData must never crash the Settings screen over a non-essential preference file.
    /// </summary>
    internal void Save(string filePath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(this, WriteOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
