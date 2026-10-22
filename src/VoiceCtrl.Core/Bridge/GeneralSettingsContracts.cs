using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Core.Bridge;

// The rest of the Settings page (slice 23): Sound and Overlay, Privacy, App and the About card's
// facts. One view for all of them, since each is a switch or a choice read straight from settings.

/// <param name="SoundCues">dictation.soundCues.</param>
/// <param name="OverlayDock">Follows a drag of the Overlay too.</param>
/// <param name="Updates">The Automatic updates row.</param>
/// <param name="EngineRecord">privacy.engineRecord: the opt-in, local record of what the voice engine did.</param>
public sealed record GeneralSettingsView(
    bool SoundCues,
    OverlayDock OverlayDock,
    bool AlwaysShowOverlay,
    KeepDictations KeepDictations,
    bool StartWithWindows,
    UpdateView Updates,
    bool EngineRecord);

[BridgeCommand("settings.getGeneral", Result = typeof(GeneralSettingsView))]
public sealed record GetGeneralSettings;

/// <summary>Any of the group's settings changed (here, by dragging the Overlay, or elsewhere), or
/// an update check or download moved on.</summary>
[BridgeEvent("settings.general")]
public sealed record GeneralSettingsChanged(GeneralSettingsView General);

[BridgeCommand("settings.setSoundCues")]
public sealed record SetSoundCues(bool On);

/// <summary>The Sound Cues row's Play: the start and done cues, whether or not the switch is on.</summary>
[BridgeCommand("settings.playSoundCues")]
public sealed record PlaySoundCues;

[BridgeCommand("settings.setOverlayDock")]
public sealed record SetOverlayDock(OverlayDock Dock);

[BridgeCommand("settings.setAlwaysShowOverlay")]
public sealed record SetAlwaysShowOverlay(bool On);

/// <summary>How many kept Dictations a Keep Dictations choice would delete from this PC.</summary>
public sealed record KeepDictationsImpact(int Removes);

/// <summary>Asked before a shorter Keep Dictations is saved, so the page can say what it deletes.</summary>
[BridgeCommand("settings.previewKeepDictations", Result = typeof(KeepDictationsImpact))]
public sealed record PreviewKeepDictations(KeepDictations Keep);

/// <summary>Saves Keep Dictations; a shorter choice deletes at once, on disk.</summary>
[BridgeCommand("settings.setKeepDictations")]
public sealed record SetKeepDictations(KeepDictations Keep);

/// <summary>The Engine record row's switch. Turning it off deletes the record from this PC at once.</summary>
[BridgeCommand("settings.setEngineRecord")]
public sealed record SetEngineRecord(bool On);

/// <summary>Your data's Open folder: File Explorer at the data folder.</summary>
[BridgeCommand("settings.openDataFolder")]
public sealed record OpenDataFolder;

public sealed record CopyDiagnosticsResult(bool Copied);

/// <summary>Builds the scrubbed report (spec §5.3) and puts it on the clipboard.</summary>
[BridgeCommand("settings.copyDiagnostics", Result = typeof(CopyDiagnosticsResult))]
public sealed record CopyDiagnostics;

/// <summary>Saves Start VoiceCtrl with Windows and writes or removes the Run value at once.</summary>
[BridgeCommand("settings.setStartWithWindows")]
public sealed record SetStartWithWindows(bool On);

/// <summary>Check now. Progress arrives as <c>settings.general</c>.</summary>
[BridgeCommand("updates.check")]
public sealed record CheckForUpdates;

/// <summary>"Download and install" on a version a check found while Automatic updates is off.</summary>
[BridgeCommand("updates.download")]
public sealed record DownloadUpdate;

[BridgeCommand("updates.setAutomatic")]
public sealed record SetAutomaticUpdates(bool On);

/// <summary>Ready's Restart now: installs the downloaded update and restarts VoiceCtrl.</summary>
[BridgeCommand("updates.restart")]
public sealed record RestartToUpdate;
