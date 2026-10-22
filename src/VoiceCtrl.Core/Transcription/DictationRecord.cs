using System.Text.Json.Serialization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>One entry in the Dictations history: the Transcription text, when it happened and
/// which app it was for. Never audio (CONTEXT: Dictations).</summary>
public sealed class DictationRecord
{
    /// <summary>What the Dictations screen names a row by. v3's history.json has no ids, so a record
    /// read from it gets a fresh one, which the next save writes back.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Text { get; init; }

    public required DateTime TimestampUtc { get; init; }

    /// <summary>The app that was in front when the Dictation started (spec §5.3), for
    /// "time · app". Null for v3 records and when the app couldn't be read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppName { get; init; }

    /// <summary>The text before Smart cleanup edited it, when it did. The Dictations screen offers it
    /// back with one click. Kept and dropped with the Dictation, under Keep Dictations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Original { get; init; }
}
