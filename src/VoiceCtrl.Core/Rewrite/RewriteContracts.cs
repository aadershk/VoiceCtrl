namespace VoiceCtrl.Core.Rewrite;

/// <summary>How a rewrite ended, short of an exception. The words for each are the Overlay page's.</summary>
public enum RewriteOutcomeKind
{
    /// <summary><see cref="RewriteOutcome.Text"/> replaces the source.</summary>
    Rewritten,

    /// <summary>Nothing intelligible was said: there is no instruction to follow.</summary>
    NoInstruction,

    /// <summary>Offline, and the local language model is not on disk.</summary>
    NeedsLanguageModel,

    /// <summary>The model timed out or answered with something that is not an edit of the text.
    /// The source stays as it was.</summary>
    CouldNotRewrite,
}

public sealed record RewriteOutcome(RewriteOutcomeKind Kind, string? Text = null)
{
    public static RewriteOutcome Rewritten(string text) => new(RewriteOutcomeKind.Rewritten, text);

    public static readonly RewriteOutcome NoInstruction = new(RewriteOutcomeKind.NoInstruction);
    public static readonly RewriteOutcome NeedsLanguageModel = new(RewriteOutcomeKind.NeedsLanguageModel);
    public static readonly RewriteOutcome CouldNotRewrite = new(RewriteOutcomeKind.CouldNotRewrite);
}

/// <summary>
/// Applies a spoken instruction ("make this shorter", "turn this into bullets", "translate to
/// Dutch") to a text: Gemini Online, the local language model Offline. A rewrite is meant to change
/// words, so unlike Smart cleanup it has no word guard; what bounds it is the user's own view of the
/// result, the kept Original, and the app's undo.
/// </summary>
public interface IRewriteClient
{
    /// <summary>Hears <paramref name="instructionAudio"/> (a WAV clip) and applies it to
    /// <paramref name="source"/>. <paramref name="heard"/> is the instruction's words when
    /// <see cref="HearAsync"/> already wrote them down, so the speech model need not run twice.
    /// Throws what the transcription clients throw for a network or service failure.</summary>
    Task<RewriteOutcome> RewriteAsync(byte[] instructionAudio, string source, CancellationToken cancellationToken, string? heard = null);

    /// <summary>The instruction's words alone, nothing applied: for an instruction that may ask for
    /// a new email rather than an edit (ticket 57). Null when there was no speech. Throws as
    /// <see cref="RewriteAsync"/> does.</summary>
    Task<string?> HearAsync(byte[] instructionAudio, CancellationToken cancellationToken);

    /// <summary>Best-effort warm-up while the user is still speaking: connection or model load.
    /// Never throws.</summary>
    Task PrewarmAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>The focused app's selection, read and re-made the only way every app allows: through
/// the clipboard and the keyboard.</summary>
public interface ISelectionAccess
{
    /// <summary>The text selected in the focused app, or null when nothing is selected (or the app
    /// can't be read, e.g. it runs elevated). The clipboard is put back as it was.</summary>
    Task<string?> CopySelectionAsync(CancellationToken cancellationToken);

    /// <summary>Selects <paramref name="text"/>, which was pasted just before the caret, and checks
    /// the selection really is that text. False, with the caret where it was, when it is not (the
    /// user moved on).</summary>
    Task<bool> SelectPrecedingAsync(string text, CancellationToken cancellationToken);
}
