using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Bridge;

// The settings.* and mic.* contracts for the Settings page (spec §4). The Dictation group comes
// first (slice 20); later Settings slices add their groups here, additively.

/// <summary>The Dictation group as the page shows it: keys as one chip per key.</summary>
/// <param name="MicrophoneId">Null: the Windows default.</param>
/// <param name="MicrophoneName">The chosen device's name when chosen, shown while it is unplugged.</param>
/// <param name="TriggerKey">Null once the Trigger Key was removed (or is incomplete).</param>
/// <param name="RewriteKey">Null once the Rewrite key was removed.</param>
/// <param name="PasteAgainTaken">Windows refused the Paste Again key: another app owns it.</param>
/// <param name="TriggerKeyConflict">A plain-language notice when the talk keys can clash with a
/// Windows shortcut (VED-74); null when nothing is known to. Never blocks the choice.</param>
/// <param name="RewriteKeyConflict">The same for the Rewrite key.</param>
/// <param name="MuteOtherAudio">Its row is in the Sound and Overlay group, but the setting is the
/// Dictation section's.</param>
public sealed record DictationSettingsView(
    string? MicrophoneId,
    string? MicrophoneName,
    IReadOnlyList<string> Hotkey,
    IReadOnlyList<string>? TriggerKey,
    IReadOnlyList<string> PasteAgain,
    bool PasteAgainTaken,
    bool PressEnter,
    bool MuteOtherAudio,
    IReadOnlyList<string>? RewriteKey,
    string? TriggerKeyConflict = null,
    string? RewriteKeyConflict = null)
{
    public static DictationSettingsView From(DictationSection section, bool pasteAgainTaken, bool multipleInputLanguages = false)
    {
        HotkeySettings hotkeys = section.Hotkeys;
        IReadOnlyList<string>? trigger = hotkeys.IsTriggerReady ? hotkeys.TriggerChips : null;
        return new DictationSettingsView(
            section.MicrophoneId,
            section.MicrophoneName,
            [hotkeys.HotkeyKey],
            trigger,
            section.PasteAgain.Chips,
            pasteAgainTaken,
            section.PressEnter,
            section.MuteOtherAudio,
            hotkeys.RewriteEnabled ? hotkeys.RewriteChips : null,
            hotkeys.IsTriggerReady ? HotkeyConflicts.For(hotkeys.TriggerVks, multipleInputLanguages) : null,
            hotkeys.RewriteEnabled ? HotkeyConflicts.For(hotkeys.RewriteKeys, multipleInputLanguages) : null);
    }
}

[BridgeCommand("settings.getDictation", Result = typeof(DictationSettingsView))]
public sealed record GetDictationSettings;

/// <summary>The Dictation section changed (here, or from Setup or the tray).</summary>
[BridgeEvent("settings.dictation")]
public sealed record DictationSettingsChanged(DictationSettingsView Dictation);

/// <summary>Chooses the microphone; null goes back to the Windows default. The host answers null
/// and raises <c>settings.dictation</c>.</summary>
[BridgeCommand("settings.setMicrophone")]
public sealed record SetMicrophone(string? Id);

[BridgeCommand("settings.setPressEnter")]
public sealed record SetPressEnter(bool On);

[BridgeCommand("settings.setMuteOtherAudio")]
public sealed record SetMuteOtherAudio(bool On);

/// <summary>Turns the Trigger Key off (the row's Remove).</summary>
[BridgeCommand("settings.clearTriggerKey")]
public sealed record ClearTriggerKey;

/// <summary>Turns the Rewrite key off (the row's Remove).</summary>
[BridgeCommand("settings.clearRewriteKey")]
public sealed record ClearRewriteKey;

public sealed record KeyCaptureOutcome(bool Saved, string? Warning);

/// <summary>
/// "Press a key": the host listens to the keyboard itself, so every key (Win and Alt included)
/// is read and none reaches another app. Answers once a key is saved or the listening ends (Esc,
/// Cancel, or the Hub losing focus). Unusable keys arrive as <c>settings.captureHint</c> meanwhile.
/// </summary>
[BridgeCommand("settings.captureKey", Result = typeof(KeyCaptureOutcome))]
public sealed record CaptureKey(KeyCaptureTarget Target);

/// <summary>The row's Cancel. The pending <c>settings.captureKey</c> answers "not saved".</summary>
[BridgeCommand("settings.cancelCapture")]
public sealed record CancelKeyCapture;

[BridgeEvent("settings.captureHint")]
public sealed record KeyCaptureHint(string Message);

public sealed record MicrophonesView(IReadOnlyList<MicrophoneDevice> Devices, MicrophoneDevice? Default)
{
    public static MicrophonesView From(MicrophoneList list) => new(list.Devices, list.Default);
}

[BridgeCommand("mic.list", Result = typeof(MicrophonesView))]
public sealed record GetMicrophones;

/// <summary>A microphone came, went or became the Windows default.</summary>
[BridgeEvent("mic.devices")]
public sealed record MicrophonesChanged(MicrophonesView Microphones);

/// <summary>The microphone row came on or off screen: the level runs only while it is visible.</summary>
[BridgeCommand("mic.preview")]
public sealed record SetMicPreview(bool On);

/// <summary>0..1, about 20 times a second while the preview runs.</summary>
[BridgeEvent("mic.level")]
public sealed record MicLevel(float Level);

// The Transcription group (slice 22): Mode, the Online provider and its key (ticket 60), Default
// cleanup and the Offline language packs. The rules are in TranscriptionSettingsService; these are the wire shapes.

[BridgeCommand("settings.getTranscription", Result = typeof(TranscriptionSettingsView))]
public sealed record GetTranscriptionSettings;

/// <summary>The group changed: a setting, the key's state, or a language pack's download.</summary>
[BridgeEvent("settings.transcription")]
public sealed record TranscriptionSettingsChanged(TranscriptionSettingsView Transcription);

[BridgeCommand("settings.setMode")]
public sealed record SetMode(TranscriptionModePreference Mode);

/// <summary><paramref name="Level"/> is verbatim, light, standard or aggressive: the names
/// settings.json stores ("Word for word" and "Thorough" are labels).</summary>
[BridgeCommand("settings.setDefaultCleanup")]
public sealed record SetDefaultCleanup(string Level);

/// <summary>Chooses the Online provider; each keeps its own key.</summary>
[BridgeCommand("settings.setProvider")]
public sealed record SetProvider(OnlineProvider Provider);

/// <summary>Checks the key with the chosen provider and saves it unless the provider refused it.
/// Answers Works, DidNotWork (nothing saved) or NotChecked (no internet, or busy: saved anyway).</summary>
[BridgeCommand("settings.saveOnlineKey", Result = typeof(KeyCheckResult))]
public sealed record SaveOnlineKey(string Key);

/// <summary>Removes the chosen provider's key; the others keep theirs.</summary>
[BridgeCommand("settings.removeOnlineKey")]
public sealed record RemoveOnlineKey;

/// <summary>What saving the Other provider's address found: null when Other has no key to check yet.</summary>
public sealed record OtherProviderSaved(SetupKeyCheck? Check);

/// <summary>Saves the Other provider's address and model (and an optional text model), then checks
/// Other's key against them when one is saved.</summary>
[BridgeCommand("settings.setOtherProvider", Result = typeof(OtherProviderSaved))]
public sealed record SetOtherProvider(string BaseUrl, string Model, string TextModel);

/// <summary>Installs a language pack (replacing the installed one), or None to go back to English
/// only. Answers at once; progress arrives as <c>settings.transcription</c>.</summary>
[BridgeCommand("settings.pickOfflinePack")]
public sealed record PickOfflinePack(OfflinePack Pack);

/// <summary>The row's Cancel: stops the download and discards what it fetched.</summary>
[BridgeCommand("settings.cancelOfflinePack")]
public sealed record CancelOfflinePack;

/// <summary>Smart cleanup on or off (ticket 37). Turning it on with no model on disk starts the
/// download of the chosen one.</summary>
[BridgeCommand("settings.setSmartCleanup")]
public sealed record SetSmartCleanup(bool On);

/// <summary>Picks which language model Smart cleanup uses, downloading it if needed; the one
/// that runs now keeps running until the new one is complete.</summary>
[BridgeCommand("settings.pickSmartCleanup")]
public sealed record PickSmartCleanup(SmartCleanupTier Tier);

/// <summary>Cancel on the Smart cleanup download: discards what it fetched.</summary>
[BridgeCommand("settings.cancelSmartCleanup")]
public sealed record CancelSmartCleanup;

/// <summary>Deletes the Smart cleanup model and turns the feature off.</summary>
[BridgeCommand("settings.removeSmartCleanup")]
public sealed record RemoveSmartCleanup;

// The Word suggestions group (slice 24): Suggest Dictionary words and the TypeSafe key. The rules
// are in DictionarySuggestions; these are the wire shapes.

[BridgeCommand("settings.getSuggestions", Result = typeof(SuggestionSettingsView))]
public sealed record GetSuggestionSettings;

/// <summary>The group changed: the switch, or the TypeSafe key or what is known of it.</summary>
[BridgeEvent("settings.suggestions")]
public sealed record SuggestionSettingsChanged(SuggestionSettingsView Suggestions);

/// <summary>"Suggest Dictionary words", from Settings or the Dictionary screen's Turn on.</summary>
[BridgeCommand("settings.setSuggestWords")]
public sealed record SetSuggestWords(bool On);

/// <summary>Checks the key with TypeSafe and saves it unless TypeSafe refused it (Offline Mode
/// saves it unchecked). Answers Works, DidNotWork (nothing saved) or NotChecked (saved anyway).</summary>
[BridgeCommand("settings.saveTypeSafeKey", Result = typeof(KeyCheckResult))]
public sealed record SaveTypeSafeKey(string Key);

[BridgeCommand("settings.removeTypeSafeKey")]
public sealed record RemoveTypeSafeKey;
