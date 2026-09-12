namespace VoiceCtrl.Core.Transcription;

/// <summary>One entry in the Dictations history: the Transcription text and when it happened.
/// Never audio — see CONTEXT.md's Dictations definition.</summary>
public sealed class DictationRecord
{
    public required string Text { get; init; }

    public required DateTime TimestampUtc { get; init; }
}
