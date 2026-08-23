namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Keeps the most recent transcript in memory so it can be recovered from the tray.
///
/// The point is that dictated words are not repeatable in the way a keystroke is: the user has
/// already said the sentence, and if the paste lands in the wrong window, gets swallowed by a
/// clipboard conflict, or goes somewhere they did not expect, the only way to get it back is to
/// say the whole thing again. Deliberately in memory only, since writing every transcript to disk
/// would turn a dictation tool into a log of everything the user has ever said.
/// </summary>
public sealed class LastTranscriptionStore
{
    private readonly object _gate = new();
    private string? _text;

    public string? Text
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }
    }

    public void Set(string text)
    {
        lock (_gate)
        {
            _text = text;
        }
    }
}
