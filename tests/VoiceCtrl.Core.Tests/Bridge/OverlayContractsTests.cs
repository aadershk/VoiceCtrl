using System.Text.Json;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Bridge;

/// <summary>What the Overlay page receives: the engine's <see cref="OverlayView"/> as a flat,
/// page-shaped state (spec §4, §7).</summary>
public class OverlayContractsTests
{
    [Fact]
    public void Listening_CarriesItsControlsAndWhenRecordingBegan()
    {
        var since = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var view = new OverlayView
        {
            Phase = OverlayPhase.Listening,
            Controls = OverlayControls.Timer | OverlayControls.Cancel,
            ListeningSince = since,
        };

        OverlayState state = OverlayState.From(view, seq: 7);

        Assert.Equal(OverlayPhase.Listening, state.Phase);
        Assert.Equal(7, state.Seq);
        Assert.True(state.ShowsTimer);
        Assert.True(state.CanCancel);
        Assert.False(state.CanStart || state.CanRetry || state.CanDismiss);
        Assert.Equal(since.ToUnixTimeMilliseconds(), state.ListeningSinceMs);
    }

    [Fact]
    public void Failed_CarriesItsReasonAndTheKeptDictationsId()
    {
        var id = Guid.NewGuid();
        var view = new OverlayView
        {
            Phase = OverlayPhase.Failed,
            Controls = OverlayControls.Retry | OverlayControls.Dismiss,
            FailureReason = FailureReason.TimedOut,
            FailedId = id,
        };

        OverlayState state = OverlayState.From(view, seq: 1);

        Assert.True(state.CanRetry);
        Assert.True(state.CanDismiss);
        Assert.Equal(FailureReason.TimedOut, state.FailureReason);
        Assert.Equal(id.ToString(), state.FailedId);
        Assert.Null(state.ListeningSinceMs);
    }

    [Fact]
    public void TheCancelledNotice_CarriesUndoAndTheKeptRecordingsId()
    {
        var id = Guid.NewGuid();
        var view = new OverlayView
        {
            Phase = OverlayPhase.Notice,
            Notice = OverlayNotice.CancelledUndo,
            Controls = OverlayControls.Undo,
            FailedId = id,
        };

        OverlayState state = OverlayState.From(view, seq: 2);

        Assert.True(state.CanUndo);
        Assert.False(state.CanRetry || state.CanCancel);
        Assert.Equal(id.ToString(), state.FailedId);
    }

    [Fact]
    public void OnTheWire_EnumsAreCamelCaseStrings()
    {
        var view = new OverlayView { Phase = OverlayPhase.Notice, Notice = OverlayNotice.CopiedPressCtrlV, AfterCancel = true };

        string json = BridgeDispatcher.EventMessage(new OverlayViewChanged(OverlayState.From(view, seq: 3)));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement payload = document.RootElement.GetProperty("payload").GetProperty("state");
        Assert.Equal("overlay.view", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("notice", payload.GetProperty("phase").GetString());
        Assert.Equal("copiedPressCtrlV", payload.GetProperty("notice").GetString());
        Assert.True(payload.GetProperty("afterCancel").GetBoolean());
    }

    [Fact]
    public void Pointer_KindsTravelAsCamelCaseStrings()
    {
        string json = BridgeDispatcher.EventMessage(new OverlayPointer(OverlayPointerKind.Leave, 12.5, 3));

        Assert.Contains("\"kind\":\"leave\"", json, StringComparison.Ordinal);
    }
}
