using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Rewrite;

namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// Rewrite by voice (ticket 39): hold the Rewrite key, say what to do to a text ("make this shorter",
/// "turn this into bullets", "translate to Dutch"), let go, and the result replaces it. The text is
/// the selection, or with nothing selected the last Dictation; in an email app with nothing selected
/// the instruction may instead ask for a new email (DictationController.EmailRequest.cs). The words
/// before are kept as the Dictation's Original, and the paste is one undo step in the app, so a
/// rewrite is never final.
/// Kept apart so the controller stays under the 500-line limit.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>Rewrite key hold: starts recording the instruction at once from any state, unless a
    /// Dictation or another rewrite is already Listening or Transcribing. Does nothing when the
    /// feature has no rewriter or selection reader.</summary>
    public Task RewriteHoldStart() =>
        _active is { IsListening: false }
            ? PillPressed()
            : _collaborators is { Rewriter: not null, Selection: not null } && View.Phase is not (OverlayPhase.Listening or OverlayPhase.Transcribing)
                ? StartDictation(startedByHold: true, isRewrite: true)
                : Task.CompletedTask;

    /// <summary>Rewrite key release: ends the instruction its own hold started, and only that.</summary>
    public Task RewriteHoldEnd() =>
        View.Phase == OverlayPhase.Listening && _active is { StartedByHold: true, IsRewrite: true }
            ? EndHold(_active)
            : Task.CompletedTask;

    /// <summary>Another key was pressed during the Rewrite hold, so it was a shortcut (Ctrl + Shift +
    /// arrow), not a rewrite: throws the recording away. Only while it is still being recorded, and
    /// never a Dictation.</summary>
    public void CancelRewrite()
    {
        if (View.Phase == OverlayPhase.Listening && _active is { IsRewrite: true })
        {
            Cancel();
        }
    }

    /// <summary>The tail of a rewrite: find the text, have the instruction applied, paste the result
    /// over the text, and show how it went. Never changes the user's text unless it pastes.</summary>
    private async Task RewriteAndPasteAsync(ActiveDictation dictation, byte[] instructionAudio)
    {
        CancellationToken token = dictation.Cancellation.Token;
        ISelectionAccess selection = _collaborators.Selection!;

        string? selected;
        try
        {
            selected = await selection.CopySelectionAsync(token).ConfigureAwait(true);
        }
        catch (Exception) when (IsSuperseded(dictation))
        {
            return;
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("RewriteSelection", ex);
            selected = null;
        }

        if (IsSuperseded(dictation))
        {
            return;
        }

        // The last Dictation is what the user means by "just dictated, now change it" with nothing
        // selected; it is only selected in the app once there is a rewrite to put over it.
        bool fromSelection = !string.IsNullOrEmpty(selected);

        // Nothing selected in an email app: the instruction may ask for a new email (ticket 57), so it
        // is heard first. A request is written at the caret; anything else edits the last Dictation.
        string? heard = null;
        if (!fromSelection && EmailWriterHere() is { } writer)
        {
            (bool done, heard) = await WriteEmailIfRequestedAsync(dictation, writer, instructionAudio, token).ConfigureAwait(true);
            if (done)
            {
                return;
            }
        }

        string? source = fromSelection ? selected : _collaborators.LastTranscription.Text;
        if (string.IsNullOrEmpty(source))
        {
            _active = null;
            ShowNotice(OverlayNotice.NothingToRewrite);
            return;
        }

        RewriteOutcome outcome;
        try
        {
            outcome = await _collaborators.Rewriter!.RewriteAsync(instructionAudio, source, token, heard).ConfigureAwait(true);
        }
        catch (Exception) when (IsSuperseded(dictation))
        {
            return;
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Rewrite", ex);
            _active = null;
            ShowNotice(OverlayNotice.RewriteFailed);
            return;
        }

        if (IsSuperseded(dictation))
        {
            return;
        }

        if (outcome.Kind != RewriteOutcomeKind.Rewritten || string.IsNullOrEmpty(outcome.Text))
        {
            _active = null;
            ShowRewriteOutcome(outcome.Kind);
            return;
        }

        if (!fromSelection && !await SelectLastDictationAsync(dictation, source, token).ConfigureAwait(true))
        {
            return;
        }

        _active = null;
        string rewritten = outcome.Text;

        // Kept before the paste, like a Dictation: Paste Again can recover it if the paste misses,
        // and the next rewrite or Dictations screen finds it. The text before is its Original.
        _collaborators.LastTranscription.Set(rewritten);
        _collaborators.History.Add(rewritten, dictation.AppName, original: source);

        InjectionResult result = await _collaborators.Injector.InjectAsync(rewritten).ConfigureAwait(true);
        switch (result)
        {
            case InjectionResult.Injected:
                _collaborators.SoundCues.Play(SoundCue.Done);
                ShowForAWhile(new OverlayView { Phase = OverlayPhase.Pasted }, PastedDuration);
                break;

            default:
                ShowPasteNotice(result);
                break;
        }
    }

    /// <summary>Selects the last Dictation, which sits just before the caret, so the paste replaces
    /// it. False (and a notice) when it is not there, so nothing is replaced by mistake.</summary>
    private async Task<bool> SelectLastDictationAsync(ActiveDictation dictation, string lastDictation, CancellationToken token)
    {
        bool selected;
        try
        {
            selected = await _collaborators.Selection!.SelectPrecedingAsync(lastDictation, token).ConfigureAwait(true);
        }
        catch (Exception) when (IsSuperseded(dictation))
        {
            return false;
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("RewriteSelectLast", ex);
            selected = false;
        }

        if (IsSuperseded(dictation))
        {
            return false;
        }

        if (!selected)
        {
            _active = null;
            ShowNotice(OverlayNotice.NothingToRewrite);
        }

        return selected;
    }

    private void ShowRewriteOutcome(RewriteOutcomeKind kind)
    {
        switch (kind)
        {
            case RewriteOutcomeKind.NoInstruction:
                ShowForAWhile(new OverlayView { Phase = OverlayPhase.NoSpeech }, NoSpeechDuration);
                break;

            case RewriteOutcomeKind.NeedsLanguageModel:
                ShowNotice(OverlayNotice.RewriteNeedsLanguageModel);
                break;

            default:
                ShowNotice(OverlayNotice.RewriteFailed);
                break;
        }
    }
}
