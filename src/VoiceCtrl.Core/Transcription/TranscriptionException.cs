namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Base type for provider-specific transcription failures. Callers (the overlay's error handling)
/// catch this instead of a concrete provider exception, so a second <see cref="ITranscriptionClient"/>
/// implementation can plug in without the UI layer knowing which provider is active.
/// </summary>
public class TranscriptionException : Exception
{
    public TranscriptionException(string message) : base(message)
    {
    }

    public TranscriptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
