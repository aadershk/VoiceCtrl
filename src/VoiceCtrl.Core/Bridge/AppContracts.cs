using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Status;

namespace VoiceCtrl.Core.Bridge;

// The app.* and window.* contracts every surface shares. Each later slice adds its own file here
// for its namespace (additive edits only; see the map's "Shared files" rule), then registers the
// new types in BridgeJsonContext and regenerates contracts.g.ts with tools/BridgeTypegen.

/// <summary>What the host sends in answer to <c>app.ready</c>: everything a page renders before
/// any event. Surfaces that need more get their own snapshot fields as their slices land.</summary>
/// <param name="UpdatedTo">The Hub sidebar's "Updated to" card (DESIGN §12): the version it
/// announces, or null for no card.</param>
public sealed record AppSnapshot(
    Theme Theme,
    ThemePreference ThemePreference,
    bool ReducedMotion,
    string Version,
    AppStatus Status,
    string? UpdatedTo);

/// <summary>The handshake: a page's first message. The host answers with the full snapshot.</summary>
[BridgeCommand("app.ready", Result = typeof(AppSnapshot))]
public sealed record AppReady;

/// <summary>Saves the Theme preference. The host answers null and then raises <c>app.theme</c>
/// to every open surface, including the one that asked.</summary>
[BridgeCommand("app.setTheme")]
public sealed record SetTheme(ThemePreference Theme);

[BridgeCommand("window.minimize")]
public sealed record MinimizeWindow;

[BridgeCommand("window.toggleMaximize")]
public sealed record ToggleMaximizeWindow;

[BridgeCommand("window.close")]
public sealed record CloseWindow;

/// <summary>The effective Theme or the stored preference changed (Settings, or Windows itself
/// while the preference is System).</summary>
[BridgeEvent("app.theme")]
public sealed record ThemeChanged(Theme Theme, ThemePreference ThemePreference);

/// <summary>Windows' "Animation effects" setting changed.</summary>
[BridgeEvent("app.reducedMotion")]
public sealed record ReducedMotionChanged(bool ReducedMotion);

/// <summary>Pause (the status card's Resume, the tray flyout's Pause and Resume). In memory only;
/// the host answers null and raises <c>app.status</c>.</summary>
[BridgeCommand("app.setPaused")]
public sealed record SetPaused(bool Paused);

/// <summary>The status card's state changed: a microphone came or went, the key was refused or
/// accepted, Mode or the Hotkey changed, or Pause was switched.</summary>
[BridgeEvent("app.status")]
public sealed record StatusChanged(AppStatus Status);

/// <summary>The window was maximized or restored (by the page's button, Snap, or a title-strip
/// double-click), so the page shows the matching caption glyph.</summary>
[BridgeEvent("window.state")]
public sealed record WindowStateChanged(bool Maximized);

/// <summary>The update card's ✕ or See what's new: the card is gone for good. The page hides it
/// straight away; only the Hub shows it.</summary>
[BridgeCommand("app.dismissUpdateCard")]
public sealed record DismissUpdateCard;
