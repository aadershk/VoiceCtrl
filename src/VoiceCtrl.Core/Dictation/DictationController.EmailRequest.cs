using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// An Email request (ticket 57, ADR-0005): hold the Rewrite key with nothing selected in an email
/// app, say "write an email to Mark thanking him for his time", and the email is written at the
/// caret by Gemini or the local model. Plain dictation is never read for requests, so dictated words
/// are never rewritten. The pill stays on Transcribing, saying it is writing, so Esc, a press and the
/// slow notice work as they do while transcribing. The words said are kept as the Dictation's
/// Original. Kept apart so the controller stays under the 500-line limit.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>How long writing an email runs before the pill says it is taking longer than usual.
    /// The local model took 2–8 s for most emails on the owner's PC (ticket 57), so this clears the
    /// slow ones and still speaks up well before the writer gives up at 60 s.</summary>
    internal static readonly TimeSpan WritingSlowThreshold = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Nothing is selected and the app is an email app: hears the instruction, and when it asks for a
    /// new email writes and pastes it. Done: the rewrite is over (written, failed, cancelled or no
    /// speech). Otherwise the instruction is an edit of the last Dictation, and its words are handed
    /// back so the speech model need not hear it twice.
    /// </summary>
    private async Task<(bool Done, string? Heard)> WriteEmailIfRequestedAsync(
        ActiveDictation dictation, IEmailWriter writer, byte[] instructionAudio, CancellationToken token)
    {
        string? heard;
        try
        {
            heard = await _collaborators.Rewriter!.HearAsync(instructionAudio, token).ConfigureAwait(true);
        }
        catch (Exception) when (IsSuperseded(dictation))
        {
            return (true, null);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("EmailRequestHear", ex);
            _active = null;
            ShowNotice(OverlayNotice.RewriteFailed);
            return (true, null);
        }

        if (IsSuperseded(dictation))
        {
            return (true, null);
        }

        if (string.IsNullOrWhiteSpace(heard))
        {
            _active = null;
            ShowForAWhile(new OverlayView { Phase = OverlayPhase.NoSpeech }, NoSpeechDuration);
            return (true, null);
        }

        EmailRequest? request;
        try
        {
            request = writer.ForInstruction(heard);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("EmailRequestRecognise", ex);
            request = null;
        }

        if (request is null)
        {
            return (false, heard);
        }

        BeginWriting(dictation);
        string? email;
        try
        {
            email = await writer.WriteAsync(request, token).ConfigureAwait(true);
        }
        catch (Exception) when (IsSuperseded(dictation))
        {
            return (true, null);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("EmailRequestWrite", ex);
            email = null;
        }

        if (IsSuperseded(dictation))
        {
            return (true, null);
        }

        _active = null;
        if (email is null)
        {
            ShowNotice(OverlayNotice.EmailNotWritten);
            return (true, null);
        }

        // Kept before the paste, like a Dictation, so Paste Again and the Dictations screen can recover
        // it; the words said are its Original.
        _collaborators.LastTranscription.Set(email);
        _collaborators.History.Add(email, dictation.AppName, original: heard);

        InjectionResult result = await _collaborators.Injector.InjectAsync(email).ConfigureAwait(true);
        if (result == InjectionResult.Injected)
        {
            _collaborators.SoundCues.Play(SoundCue.Done);
            ShowForAWhile(new OverlayView { Phase = OverlayPhase.Pasted }, PastedDuration);
        }
        else
        {
            ShowPasteNotice(result);
        }

        return (true, null);
    }

    /// <summary>The pill says it is writing, on a clock of its own for "Taking longer than usual":
    /// the wait so far was hearing the instruction.</summary>
    private void BeginWriting(ActiveDictation dictation)
    {
        dictation.IsWriting = true;
        dictation.IsSlow = false;
        dictation.SlowTimer?.Dispose();
        dictation.SlowTimer = _collaborators.Timer.Schedule(WritingSlowThreshold, () => OnTranscribingSlow(dictation));

        // A "Still transcribing" notice up now returns to the writing pill by itself.
        if (View.Phase == OverlayPhase.Transcribing || View is { Phase: OverlayPhase.Notice, Notice: OverlayNotice.TakingLonger })
        {
            SetView(TranscribingViewFor(dictation));
        }
    }

    /// <summary>The email writer when a Rewrite key instruction with nothing selected could be an
    /// Email request here; null otherwise.</summary>
    private IEmailWriter? EmailWriterHere()
    {
        if (_collaborators.EmailWriter is not { } writer)
        {
            return null;
        }

        try
        {
            return writer.AppliesHere() ? writer : null;
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("EmailRequestApplies", ex);
            return null;
        }
    }

    /// <summary>Warms up while the Rewrite key is held: the rewrite, and in an email app the email
    /// writer too, since with nothing selected the instruction may ask for an email.</summary>
    private Task PrewarmRewriteAsync() => Task.WhenAll(
        _collaborators.Rewriter!.PrewarmAsync(),
        _collaborators.EmailWriter?.PrewarmAsync() ?? Task.CompletedTask);

    /// <summary>The Transcribing pill for <paramref name="dictation"/>: a rewrite's, an email being
    /// written, or a Dictation's.</summary>
    private static OverlayView TranscribingViewFor(ActiveDictation dictation) =>
        TranscribingView with { Rewrite = dictation.IsRewrite, Writing = dictation.IsWriting };
}
