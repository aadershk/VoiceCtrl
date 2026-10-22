namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Remembers the text Smart cleanup replaced, from the transcription client to the Dictation engine
/// that files the result, so the Dictations screen can offer the Original back. One Dictation at a
/// time moves through, so one slot is enough; the engine takes it right after the transcription and
/// every transcription clears it first, so a stale Original can never attach to the next Dictation.
/// </summary>
public sealed class SmartCleanupLedger
{
    private readonly object _gate = new();
    private string? _original;

    public void Clear()
    {
        lock (_gate)
        {
            _original = null;
        }
    }

    public void Record(string original)
    {
        lock (_gate)
        {
            _original = original;
        }
    }

    /// <summary>The recorded Original, or null when Smart cleanup left the text alone. Clears it.</summary>
    public string? Take()
    {
        lock (_gate)
        {
            string? original = _original;
            _original = null;
            return original;
        }
    }
}
