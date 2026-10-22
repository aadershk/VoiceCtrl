using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Overlay;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Every user-facing setting, as <see cref="SettingsStore"/> keeps it in settings.json (spec §5.2).
/// Immutable: a change is a new value passed to <see cref="SettingsStore.Update"/>, which is what
/// lets the store see exactly which sections changed and raise only their events.
/// <para>
/// Sections gain fields as their slices land. A new field needs a default here that is right both
/// for a fresh install and for a file written before the field existed, since a missing field
/// simply keeps its initializer.
/// </para>
/// </summary>
public sealed record UserSettings
{
    /// <summary>Bumped only when an existing field changes meaning. Adding a field never needs it.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public DictationSection Dictation { get; init; } = new();

    public TranscriptionSection Transcription { get; init; } = new();

    public OverlaySection Overlay { get; init; } = new();

    public PrivacySection Privacy { get; init; } = new();

    public AppSection App { get; init; } = new();

    /// <summary>The Style screen: each kind of app's Style, the user's name, moved apps (ticket 56).</summary>
    public Transcription.StyleSettings Style { get; init; } = new();

    public AdvancedSection Advanced { get; init; } = new();
}

/// <summary>Microphone, keys and the per-Dictation switches.</summary>
public sealed record DictationSection
{
    /// <summary>The Hotkey and Trigger Key bindings.</summary>
    public HotkeySettings Hotkeys { get; init; } = new();

    /// <summary>The chosen capture endpoint's Core Audio ID; null is the Windows default. Kept
    /// while the device is unplugged, so it is used again when it comes back.</summary>
    public string? MicrophoneId { get; init; }

    /// <summary>The chosen device's name when it was chosen, for the Settings row to name it
    /// while it is unplugged.</summary>
    public string? MicrophoneName { get; init; }

    public PasteAgainKey PasteAgain { get; init; } = new();

    /// <summary>The Warm Sound Cues (start, stop, done, failed). Off by default (research 11).</summary>
    public bool SoundCues { get; init; }

    /// <summary>A final spoken "press enter" is stripped and sent as Enter after the paste. Off by
    /// default (CONTEXT: Press Enter).</summary>
    public bool PressEnter { get; init; }

    /// <summary>Other apps drop to 20% while a Dictation records. On by default (CONTEXT: Mute
    /// Other Audio).</summary>
    public bool MuteOtherAudio { get; init; } = true;
}

/// <summary>Mode, default cleanup and the extra Offline language pack.</summary>
public sealed record TranscriptionSection
{
    public TranscriptionModePreference Mode { get; init; } = TranscriptionModePreference.Auto;

    /// <summary>The Cleanup Level a Dictation gets when no Profile sets one.</summary>
    public string DefaultCleanup { get; init; } = "standard";

    /// <summary>The installed extra language pack, which Offline then uses for every Dictation.
    /// Set only once its download is complete, so it never names a half-downloaded model.</summary>
    public OfflinePack OfflinePack { get; init; } = OfflinePack.None;

    /// <summary>Smart cleanup: a local language model edits the Offline text (retractions, lists,
    /// fillers, layout). On by default; it does nothing until its model is downloaded, and Offline
    /// then works as before (ticket 37).</summary>
    public bool SmartCleanup { get; init; } = true;

    /// <summary>Which model Smart cleanup uses. Better is the default because Standard missed the
    /// quality target (ticket 37 results).</summary>
    public SmartCleanupTier SmartCleanupTier { get; init; } = SmartCleanupTier.Better;

    /// <summary>Who Online sends to (ticket 60). Null only in a file written before the choice
    /// existed: <see cref="Online.ProviderMigration"/> settles it at start, Gemini for an install
    /// with a Gemini key and NVIDIA otherwise.</summary>
    public Online.OnlineProvider? Provider { get; init; }

    /// <summary>The Other provider's address and model; empty until the user fills them in.</summary>
    public Online.OtherProviderSettings Other { get; init; } = new();
}

/// <summary>Where the Overlay sits and whether it rests on screen.</summary>
public sealed record OverlaySection
{
    /// <summary>Set by dragging the Overlay, or in Settings.</summary>
    public OverlayDock Dock { get; init; } = OverlayDock.Bottom;

    /// <summary>"Always show the Overlay": rest as the grey handle instead of hiding. Off by default.</summary>
    public bool AlwaysShow { get; init; }
}

/// <summary>Keep Dictations and Dictionary Suggestions.</summary>
public sealed record PrivacySection
{
    /// <summary>How long Dictations stay on this PC; the last 50 by default.</summary>
    public KeepDictations KeepDictations { get; init; } = KeepDictations.Last50;

    /// <summary>"Suggest Dictionary words": the Dictionary screen offers words found in the kept
    /// Dictations. Off by default, and never used in Offline Mode (CONTEXT: Dictionary Suggestion).</summary>
    public bool SuggestDictionaryWords { get; init; }

    /// <summary>The engine record (VED-77): per Dictation, what the Offline voice engine did, as
    /// counts, word pairs and timings, never the text. Off by default; on this PC only.</summary>
    public bool EngineRecord { get; init; }
}

/// <summary>Theme, startup, updates and first-run state. The last-run version and the update
/// card arrive with the update slice.</summary>
public sealed record AppSection
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>On by default because v3's Setup always turned it on; the Setup window can turn
    /// it off. The host makes the HKCU Run key match this on every start.</summary>
    public bool StartWithWindows { get; init; } = true;

    /// <summary>False only on a true first run. Set by finishing or skipping Setup, and by the v3
    /// migration when it finds any sign of a v3 install, so upgraders go straight to the Hub.</summary>
    public bool SetupComplete { get; init; }

    /// <summary>Quiet auto-update: new versions download in the background and install at the
    /// next start. On by default (owner, 2026-09-28).</summary>
    public bool AutomaticUpdates { get; init; } = true;

    /// <summary>When a check last reached the update feed, for "Checked today at 9:41". Null
    /// until the first successful check.</summary>
    public DateTimeOffset? LastUpdateCheck { get; init; }

    /// <summary>The version that last started, so a start of a newer one knows an update was
    /// applied. Null means "show no card": a fresh install, or an upgrade from v3.</summary>
    public string? LastRunVersion { get; init; }

    /// <summary>The "Updated to" card (see <see cref="Updates.UpdateCard"/>), until it is dismissed.</summary>
    public Updates.UpdateCardState? UpdateCard { get; init; }

    /// <summary>A version the user went back from after it failed to start; checks don't offer
    /// it again. Cleared once a newer version runs.</summary>
    public string? SkippedUpdateVersion { get; init; }
}

/// <summary>
/// The v3 .env tuning keys that have no UI. Every field is an override: null means "use the
/// built-in default" (applied in <see cref="ConfigLoader.Load(UserSettings, string)"/>), so a
/// default that changes in a later release, such as a retired Gemini model, still reaches users
/// who never touched it.
/// </summary>
public sealed record AdvancedSection
{
    public string? GeminiModelId { get; init; }

    public int? DoubleTapWindowMs { get; init; }

    public int? AutoHideDelayMs { get; init; }

    public string? ThinkingLevel { get; init; }

    public string? StyleNotes { get; init; }

    public bool? EnableToneAwareness { get; init; }

    public IReadOnlyList<string>? PromptStyleApps { get; init; }

    public IReadOnlyList<string>? ChatStyleApps { get; init; }

    public string? LocalModelVariant { get; init; }

    public int? LocalNumThreads { get; init; }

    public bool? CompressUpload { get; init; }

    public bool? SpokenPunctuation { get; init; }
}
