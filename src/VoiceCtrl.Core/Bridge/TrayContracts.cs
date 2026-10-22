using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Tray;

namespace VoiceCtrl.Core.Bridge;

// The tray.* contracts (DESIGN §12, spec §7): the flyout is kept warm and hidden between uses, so
// each opening starts with tray.open (fresh Mode and Copy latest state, and the corner it grows
// from). The page lays itself out and answers tray.place with its size; the host then puts the
// window by the icon and shows it. Pause is app.setPaused and Mode is settings.setMode, the same
// commands the Hub uses.

/// <summary>The flyout is about to open. The page replays its entrance from <see cref="Corner"/>
/// and answers with <c>tray.place</c>.</summary>
/// <param name="HasLatest">Whether there is a latest Transcription to copy.</param>
[BridgeEvent("tray.open")]
public sealed record TrayOpening(TrayCorner Corner, TranscriptionModePreference Mode, bool HasLatest);

/// <summary>The page's menu size in CSS pixels, once it is laid out for this opening. The host
/// shows the window only after this, so the page is never seen half-drawn.</summary>
[BridgeCommand("tray.place")]
public sealed record PlaceTrayFlyout(double Width, double Height);

/// <summary>Esc, a click outside the menu, or any pick: the flyout hides.</summary>
[BridgeCommand("tray.close")]
public sealed record CloseTrayFlyout;

/// <summary>Open VoiceCtrl: the Hub, as a left-click on the icon opens it.</summary>
[BridgeCommand("tray.openHub")]
public sealed record OpenHubFromTray;

/// <summary>Settings: the Hub, on the Settings screen.</summary>
[BridgeCommand("tray.openSettings")]
public sealed record OpenSettingsFromTray;

/// <summary>Copy latest Transcription. A clipboard failure shows on the Overlay, not here.</summary>
[BridgeCommand("tray.copyLatest")]
public sealed record CopyLatestFromTray;

/// <summary>Quit VoiceCtrl.</summary>
[BridgeCommand("tray.quit")]
public sealed record QuitFromTray;
