using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Dictation;

/// <summary>Paste Again and the tray's Copy latest, the parts of the engine that paste outside a
/// Dictation. Kept apart so the controller stays under the 500-line limit.</summary>
public sealed partial class DictationController
{
    /// <summary>
    /// Paste Again: pastes the latest Transcription, or <paramref name="text"/> when a Dictations
    /// row chose one. Never starts a Dictation, and is ignored while one is in progress, so its
    /// clipboard work can't collide with that Dictation's paste. The Overlay appears only if the
    /// paste didn't land. Answers how the paste went, or null when there was nothing to do.
    /// </summary>
    public async Task<InjectionResult?> PasteAgain(string? text = null)
    {
        text ??= _collaborators.LastTranscription.Text;
        if (text is null || _active is not null || View.Phase is OverlayPhase.Transcribing)
        {
            return null;
        }

        InjectionResult result;
        try
        {
            result = await _collaborators.Injector.InjectAsync(text).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("PasteAgain", ex);
            result = InjectionResult.Failed;
        }

        if (result != InjectionResult.Injected && _active is null)
        {
            ShowPasteNotice(result);
        }

        return result;
    }

    /// <summary>
    /// The tray's "Copy latest" couldn't write the clipboard: says so on the Overlay (VoiceCtrl
    /// raises no Windows notifications, DESIGN §12). A Dictation in progress keeps the Overlay.
    /// </summary>
    public void ReportCopyFailed()
    {
        if (_active is null)
        {
            ShowNotice(OverlayNotice.CopyFailed);
        }
    }
}
