using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Bridge;

// The dictations.* contracts for the Dictations screen (slice 17): the kept Dictations and Failed
// Dictations as one view, and the screen's actions. The page works out day groups and "2 min ago"
// from the times; the host never formats dates.

/// <param name="AtMs">When it was transcribed, Unix milliseconds.</param>
/// <param name="App">The app it was for; null for v3 records and unreadable apps.</param>
/// <param name="HasOriginal">Smart cleanup edited it, so the row offers "Restore original".</param>
public sealed record DictationItem(Guid Id, string Text, long AtMs, string? App, bool HasOriginal);

/// <param name="AtMs">When it failed, Unix milliseconds.</param>
/// <param name="AudioSeconds">Whole seconds of kept audio, at least 1.</param>
public sealed record FailedDictationItem(Guid Id, FailureReason Reason, long AtMs, string? App, int AudioSeconds, bool Retrying);

/// <summary>Everything the Dictations screen shows.</summary>
/// <param name="Dictations">Newest first; the first is the Latest hero.</param>
/// <param name="Failed">Newest first, one notice each.</param>
/// <param name="CanRetryOffline">An offline model is installed, so "Retry offline" is offered.</param>
/// <param name="PasteAgainKeys">The Paste Again key as chips, for the hero's hint.</param>
public sealed record DictationsView(
    KeepDictations Keep,
    IReadOnlyList<DictationItem> Dictations,
    IReadOnlyList<FailedDictationItem> Failed,
    bool CanRetryOffline,
    IReadOnlyList<string> PasteAgainKeys)
{
    public static DictationsView From(
        KeepDictations keep,
        IReadOnlyList<DictationRecord> records,
        IReadOnlyList<FailedDictation> failed,
        bool canRetryOffline,
        IReadOnlyList<string> pasteAgainKeys) =>
        new(
            keep,
            records.Select(r => new DictationItem(r.Id, r.Text, new DateTimeOffset(DateTime.SpecifyKind(r.TimestampUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(), r.AppName, r.Original is not null)).ToList(),
            failed.Select(f => new FailedDictationItem(
                f.Id, f.Reason, f.FailedAtUtc.ToUnixTimeMilliseconds(), f.AppName,
                Math.Max(1, (int)Math.Round(f.AudioLength.TotalSeconds)), f.IsRetrying)).ToList(),
            canRetryOffline,
            pasteAgainKeys);
}

[BridgeCommand("dictations.get", Result = typeof(DictationsView))]
public sealed record GetDictations;

/// <summary>A Dictation was added, deleted, restored or cleared, a Failed Dictation changed, Keep
/// Dictations or the Paste Again key changed, or an offline model arrived.</summary>
[BridgeEvent("dictations.changed")]
public sealed record DictationsChanged(DictationsView Dictations);

public enum HubPasteOutcome
{
    /// <summary>Pasted into the window that was in front before the Hub.</summary>
    Pasted,

    /// <summary>That window runs as administrator: the text is on the clipboard for Ctrl+V.</summary>
    Copied,

    /// <summary>The paste didn't land; the Overlay says so.</summary>
    Failed,

    /// <summary>No other window to paste into (it closed, or none was used): copied instead.</summary>
    NoWindow,

    /// <summary>Nothing happened: the Dictation is gone, or one is being recorded right now.</summary>
    Busy,
}

/// <param name="App">The app it went to, when it went somewhere.</param>
public sealed record HubPasteResult(HubPasteOutcome Outcome, string? App);

/// <summary>Paste Again from the Hub (spec §7): brings back the window that was in front before
/// the Hub and pastes there. <paramref name="Id"/> null is the latest Transcription.</summary>
[BridgeCommand("dictations.pasteAgain", Result = typeof(HubPasteResult))]
public sealed record PasteDictationAgain(Guid? Id);

/// <summary>Copies one Dictation. Fails with <c>copy_failed</c> when the clipboard is busy.</summary>
[BridgeCommand("dictations.copy")]
public sealed record CopyDictation(Guid Id);

public sealed record CopyAllResult(int Count);

/// <summary>"Copy all as text": every kept Dictation, newest first, a blank line between.</summary>
[BridgeCommand("dictations.copyAll", Result = typeof(CopyAllResult))]
public sealed record CopyAllDictations;

/// <summary>"Restore original": the row's text becomes what was transcribed before Smart cleanup
/// edited it. Nothing happens when the row has no Original.</summary>
[BridgeCommand("dictations.restoreOriginal")]
public sealed record RestoreDictationOriginal(Guid Id);

/// <summary>Deletes one row; <c>dictations.undoDelete</c> brings it back.</summary>
[BridgeCommand("dictations.delete")]
public sealed record DeleteDictation(Guid Id);

[BridgeCommand("dictations.undoDelete")]
public sealed record UndoDeleteDictation(Guid Id);

/// <summary>Clear all Dictations, after the page's confirm. Failed Dictations stay.</summary>
[BridgeCommand("dictations.clearAll")]
public sealed record ClearAllDictations;

public sealed record RetryResult(RetryOutcome Outcome);

/// <summary>Retry or Retry offline on a Failed Dictation notice. Answers when it is done.</summary>
[BridgeCommand("dictations.retry", Result = typeof(RetryResult))]
public sealed record RetryDictation(Guid Id, bool Offline);

/// <summary>✕ on a Failed Dictation notice: drops the audio for good.</summary>
[BridgeCommand("dictations.dismiss")]
public sealed record DismissFailedDictation(Guid Id);
