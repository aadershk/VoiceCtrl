using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Overlay;

namespace VoiceCtrl.Core.Bridge;

// The overlay.* contracts (spec §7): the engine's view flattened for the page, the live level and
// forwarded pointer input one way; the page's clicks, hit rectangle and paint receipts the other.

/// <summary>
/// Everything the Overlay page renders, flattened from the engine's <see cref="OverlayView"/>.
/// <see cref="Seq"/> numbers every view, so the page can say which one it has painted (the host
/// logs show latency from that) and ignore a stale answer to <c>overlay.state</c>.
/// </summary>
public sealed record OverlayState(
    int Seq,
    OverlayPhase Phase,
    bool CanStart,
    bool ShowsTimer,
    bool CanCancel,
    bool CanRetry,
    bool CanDismiss,
    bool CanFinish,
    bool CanUndo,
    long? ListeningSinceMs,
    FailureReason? FailureReason,
    string? FailedId,
    OverlayNotice? Notice,
    string? NoticeKey,
    bool AfterCancel,
    bool Rewrite,
    bool Locked,
    bool Writing)
{
    public static OverlayState From(OverlayView view, int seq) => new(
        seq,
        view.Phase,
        CanStart: view.Controls.HasFlag(OverlayControls.Start),
        ShowsTimer: view.Controls.HasFlag(OverlayControls.Timer),
        CanCancel: view.Controls.HasFlag(OverlayControls.Cancel),
        CanRetry: view.Controls.HasFlag(OverlayControls.Retry),
        CanDismiss: view.Controls.HasFlag(OverlayControls.Dismiss),
        CanFinish: view.Controls.HasFlag(OverlayControls.Finish),
        CanUndo: view.Controls.HasFlag(OverlayControls.Undo),
        ListeningSinceMs: view.ListeningSince?.ToUnixTimeMilliseconds(),
        view.FailureReason,
        FailedId: view.FailedId?.ToString(),
        view.Notice,
        view.NoticeKey,
        view.AfterCancel,
        view.Rewrite,
        view.Locked,
        view.Writing);
}

/// <summary>The page's snapshot after <c>app.ready</c> and after a reload: the current view.</summary>
[BridgeCommand("overlay.state", Result = typeof(OverlayState))]
public sealed record GetOverlayState;

/// <summary>The engine's view changed.</summary>
[BridgeEvent("overlay.view")]
public sealed record OverlayViewChanged(OverlayState State);

/// <summary>Mic level, 0..1, while Listening (at most 60 a second).</summary>
[BridgeEvent("overlay.level")]
public sealed record OverlayLevel(double Level);

public enum OverlayPointerKind
{
    Enter,
    Leave,
    Move,
    Down,
    Up,
}

/// <summary>
/// Pointer input over the pill, forwarded by the host's mouse hook, in page CSS pixels. The
/// window is always click-through, so this is the page's only pointer input (research 15).
/// Enter and Leave bracket the hit rectangle; Down and Up are the primary button.
/// </summary>
[BridgeEvent("overlay.pointer")]
public sealed record OverlayPointer(OverlayPointerKind Kind, double X, double Y);

/// <summary>The pill's current rectangle in page CSS pixels, reported after every morph and
/// hover change. A zero size means nothing to hit.</summary>
[BridgeCommand("overlay.hitRect")]
public sealed record SetOverlayHitRect(double X, double Y, double Width, double Height);

/// <summary>The page has painted view <paramref name="Seq"/> (after its second animation frame).</summary>
[BridgeCommand("overlay.painted")]
public sealed record OverlayPainted(int Seq);

/// <summary>The Ready tile was clicked: start a Dictation.</summary>
[BridgeCommand("overlay.start")]
public sealed record StartFromOverlay;

/// <summary>✕ during a Dictation.</summary>
[BridgeCommand("overlay.cancel")]
public sealed record CancelFromOverlay;

/// <summary>The tick beside ✕ while Listening: stop and paste.</summary>
[BridgeCommand("overlay.finish")]
public sealed record FinishFromOverlay;

/// <summary>A click on the pill's body while Transcribing (not on ✕): the same as a key press
/// there, which offers Cancel.</summary>
[BridgeCommand("overlay.press")]
public sealed record PressFromOverlay;

/// <summary>Retry on the Failed notice.</summary>
[BridgeCommand("overlay.retry")]
public sealed record RetryFromOverlay(string FailedId);

/// <summary>Undo on the Cancelled notice: transcribe the cancelled recording and paste it.</summary>
[BridgeCommand("overlay.undo")]
public sealed record UndoFromOverlay(string FailedId);

/// <summary>✕ on the Failed notice: drop the kept audio.</summary>
[BridgeCommand("overlay.dismiss")]
public sealed record DismissFromOverlay(string FailedId);

/// <summary>"Add an API key" was clicked: open Settings at the Online provider's key row.</summary>
[BridgeCommand("overlay.openKeySettings")]
public sealed record OpenKeySettings;

/// <summary>
/// How the page lays the Overlay out: the dock (which way the pill grows), and whether the desktop
/// is light or dark, which tints the resting handle (DESIGN §9). The desktop follows Windows'
/// taskbar setting, since the handle sits just above the taskbar.
/// </summary>
public sealed record OverlayLayout(OverlayDock Dock, Theme Desktop);

/// <summary>The page's layout after <c>app.ready</c> and after a reload.</summary>
[BridgeCommand("overlay.layout", Result = typeof(OverlayLayout))]
public sealed record GetOverlayLayout;

/// <summary>The dock (a drag or Settings) or the desktop's light or dark setting changed.</summary>
[BridgeEvent("overlay.layoutChanged")]
public sealed record OverlayLayoutChanged(OverlayLayout Layout);

/// <summary>A press on the pill has turned into a drag (true), or the drag is over (false). While
/// it lasts the host moves the window, and the press is not a click.</summary>
[BridgeEvent("overlay.drag")]
public sealed record OverlayDragChanged(bool Dragging);

/// <summary>Moves an open Hub to a screen and, optionally, lands on one of its rows (the ids in
/// <c>ui/src/hub/screens.ts</c>). A Hub that isn't open yet gets the same target in its URL.</summary>
[BridgeEvent("hub.show")]
public sealed record ShowHubScreen(string Screen, string? Row);
