using System.Globalization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>A word the voice engine changed and the word it became. The only words the engine
/// record holds: a name here can be personal, which is why the record stays on this PC.</summary>
public sealed record WordPair(string Wrong, string Right);

/// <summary>
/// What the Offline voice engine did for one Dictation (VED-77): counts, the word pairs it changed,
/// why Smart cleanup's answer was refused and how long it all took. Never the Dictation, its
/// Original, the app it went into or a window title.
/// </summary>
/// <param name="Mend">"skipped", "ran", "timed out" or "failed": the check for mis-heard words.</param>
/// <param name="MendProposed">Pairs the model listed.</param>
/// <param name="MendRefused">Pairs the app's own checks turned down.</param>
/// <param name="MendApplied">Pairs that changed the text.</param>
/// <param name="SmartCleanup">"applied", "gated off", "guard rejected", "timed out", "failed" or "not available".</param>
/// <param name="GuardRejections">The kinds of refusal, such as "invented word"; never the word.</param>
/// <param name="Vocabulary">Words the built-in vocabulary rewrote.</param>
/// <param name="ModelMs">Time spent waiting for the language model, in milliseconds.</param>
/// <param name="TotalMs">The whole Dictation's transcription, in milliseconds.</param>
public sealed record EngineEntry(
    DateTimeOffset At,
    string Mend,
    int MendProposed,
    int MendRefused,
    IReadOnlyList<WordPair> MendApplied,
    string SmartCleanup,
    IReadOnlyList<string> GuardRejections,
    IReadOnlyList<WordPair> Vocabulary,
    int ModelMs,
    int TotalMs)
{
    /// <summary>One line of plain text, as Copy diagnostics shows it.</summary>
    public string ToLine()
    {
        string mend = $"mend: {Mend}";
        if (Mend == "ran")
        {
            mend += $", proposed {MendProposed}, refused {MendRefused}, applied {Pairs(MendApplied)}";
        }

        string cleanup = $"smart cleanup: {SmartCleanup}";
        if (GuardRejections.Count > 0)
        {
            cleanup += $" ({string.Join(", ", GuardRejections)})";
        }

        return string.Join(
            " | ",
            At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            mend,
            cleanup,
            $"vocabulary: {Pairs(Vocabulary)}",
            $"model {ModelMs} ms",
            $"total {TotalMs} ms");
    }

    private static string Pairs(IReadOnlyList<WordPair> pairs) =>
        pairs.Count == 0 ? "none" : string.Join(", ", pairs.Select(pair => $"{pair.Wrong} -> {pair.Right}"));
}
