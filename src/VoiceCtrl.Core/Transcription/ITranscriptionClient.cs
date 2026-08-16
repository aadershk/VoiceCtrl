namespace VoiceCtrl.Core.Transcription;

public interface ITranscriptionClient : IDisposable
{
    /// <summary>
    /// Transcribes and cleans up a WAV clip in one pass. Returns null if the model determined
    /// there was no discernible speech (the [NO_SPEECH_DETECTED] sentinel). Callers should treat
    /// that the same as a local silence-check failure: no injection, no error shown.
    /// </summary>
    Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort warm-up of the HTTP connection pool so the TLS/TCP handshake overlaps with the
    /// user speaking instead of the post-recording transcription call. Must never throw and must
    /// never block a recording in progress, so implementers should swallow all failures silently.
    /// </summary>
    Task PrewarmConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
