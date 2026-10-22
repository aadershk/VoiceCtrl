using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Dictation;

/// <summary>A Transcription with any final "press enter" taken off.</summary>
/// <param name="Text">What to paste; empty when the Dictation was only "press enter".</param>
/// <param name="PressEnter">Whether Enter should follow the paste.</param>
public readonly record struct PressEnterResult(string Text, bool PressEnter);

/// <summary>
/// Press Enter (CONTEXT; spec §5.3): a Dictation that ends with the spoken words "press enter"
/// is pasted without them and followed by Enter. Only the very end counts, so "press enter to
/// continue" is ordinary text.
/// </summary>
public static partial class PressEnterDetector
{
    // The words themselves, any punctuation after them, and the comma, dash or line break that
    // joined them to the sentence. A full stop, "?" or "!" before them ends the sentence the user
    // meant, so it stays.
    [GeneratedRegex(@"[\s,;:–—-]*\bpress[\s-]+enter[\s\p{P}]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FinalPressEnter();

    public static PressEnterResult Detect(string text)
    {
        Match match = FinalPressEnter().Match(text);
        return match.Success
            ? new PressEnterResult(text[..match.Index], PressEnter: true)
            : new PressEnterResult(text, PressEnter: false);
    }
}
