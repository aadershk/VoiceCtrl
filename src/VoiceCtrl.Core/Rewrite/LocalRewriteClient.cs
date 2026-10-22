using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Rewrite;

/// <summary>The local speech model, asked for the words of a spoken instruction and nothing else:
/// no Smart cleanup, since an instruction is not a Dictation to tidy.</summary>
public interface IInstructionTranscriber
{
    /// <summary>The instruction's words, or null when there was no speech.</summary>
    Task<string?> TranscribeInstructionAsync(byte[] wavBytes, CancellationToken cancellationToken);

    Task PrewarmAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Offline rewrite: the speech model writes the instruction down, the local language model (the one
/// Smart cleanup uses) applies it. It needs the model on disk but not the Smart cleanup switch,
/// which is about Dictations.
/// </summary>
internal sealed class LocalRewriteClient(IInstructionTranscriber instructions, SmartCleanupService languageModel, PersonalizationStore personalization) : IRewriteClient
{
    public Task<string?> HearAsync(byte[] instructionAudio, CancellationToken cancellationToken) =>
        instructions.TranscribeInstructionAsync(instructionAudio, cancellationToken);

    public async Task<RewriteOutcome> RewriteAsync(byte[] instructionAudio, string source, CancellationToken cancellationToken, string? heard = null)
    {
        if (!languageModel.IsInstalled)
        {
            return RewriteOutcome.NeedsLanguageModel;
        }

        if (source.Length > RewriteText.MaxOfflineChars)
        {
            return RewriteOutcome.CouldNotRewrite;
        }

        string? instruction = heard ?? await instructions.TranscribeInstructionAsync(instructionAudio, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return RewriteOutcome.NoInstruction;
        }

        string? rewritten = await languageModel.Rewrite(instruction, source, personalization.Dictionary, cancellationToken).ConfigureAwait(false);
        return rewritten is null ? RewriteOutcome.CouldNotRewrite : RewriteOutcome.Rewritten(rewritten);
    }

    public Task PrewarmAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            instructions.PrewarmAsync(cancellationToken),
            languageModel.IsInstalled ? languageModel.PrimeRewrite(personalization.Dictionary, cancellationToken) : Task.CompletedTask);
}
