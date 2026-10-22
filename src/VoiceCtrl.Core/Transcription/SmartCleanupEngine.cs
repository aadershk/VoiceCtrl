using System.Text.RegularExpressions;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>One call to the local language model: the rules, a few worked examples, the transcript.</summary>
/// <param name="MaxTokens">How long an answer may be; a copy of the input plus a little never needs more.</param>
internal sealed record ModelRequest(
    string System, IReadOnlyList<(string User, string Assistant)> Examples, string User, int MaxTokens)
{
    /// <summary>For a prime only: the server slot that reads these rules and keeps them on disk, so a
    /// restarted model has them back in a fraction of a second instead of reading them again (36 s cold
    /// on the Better model). Never set for a Dictation, whose own words must not be written to disk.</summary>
    public int? KeepInSlot { get; init; }
}

/// <summary>The local language model behind Smart cleanup. A seam so the engine is tested without
/// starting a process.</summary>
internal interface ISmartCleanupModel : IDisposable
{
    /// <summary>The model's reply, or null when it gave none.</summary>
    Task<string?> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

/// <param name="Applied">True when the model's text replaced <paramref name="Text"/>'s input.</param>
/// <param name="Reason">Why the rule-cleaned text was kept, for the log. Null when applied.</param>
internal sealed record SmartCleanupResult(string Text, bool Applied, string? Reason);

/// <summary>
/// Smart cleanup (VED-58): the rules have already cleaned the transcript, so this only improves on
/// that, and every way it can go wrong ends in the rule-cleaned text. The gate skips the model when
/// it cannot help, a timeout bounds the wait, and the guard refuses invented or dropped words.
/// </summary>
internal sealed partial class SmartCleanupEngine
{
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<ISmartCleanupModel?> _model;
    private readonly Func<int, TimeSpan> _timeoutFor;

    /// <param name="model">The model to use now, or null when none is installed or Smart cleanup is off.</param>
    /// <param name="timeoutFor">The wait for a transcript of this many words. Defaults to <see cref="DefaultTimeout"/>.</param>
    public SmartCleanupEngine(Func<ISmartCleanupModel?> model, Func<int, TimeSpan>? timeoutFor = null)
    {
        _model = model;
        _timeoutFor = timeoutFor ?? DefaultTimeout;
    }

    /// <summary>1 s plus 0.08 s a word, at most 5 s (research 33 §4): 3.2 s for a 10 s dictation.</summary>
    public static TimeSpan DefaultTimeout(int words) =>
        TimeSpan.FromSeconds(Math.Min(MaxTimeout.TotalSeconds, 1 + 0.08 * words));

    public async Task<SmartCleanupResult> CleanAsync(
        string ruleCleanedText,
        PromptContext context,
        string styleNotes,
        IReadOnlyList<DictionaryEntry> dictionary,
        CancellationToken cancellationToken)
    {
        if (!SmartCleanupGate.ShouldRun(ruleCleanedText, context))
        {
            return new SmartCleanupResult(ruleCleanedText, false, "gate: nothing to improve");
        }

        ISmartCleanupModel? model = _model();
        if (model is null)
        {
            return new SmartCleanupResult(ruleCleanedText, false, "no model");
        }

        int words = ruleCleanedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        ModelRequest request = SmartCleanupPrompt.Request(
            context, styleNotes, dictionary, ruleCleanedText, maxTokens: (int)(ruleCleanedText.Length / 2.5 * 1.5) + 16);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeoutFor(words));

        string? reply;
        try
        {
            reply = await model.CompleteAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Kept(ruleCleanedText, "timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("SmartCleanup", ex);
            return Kept(ruleCleanedText, $"model failed: {ex.GetType().Name}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string answer = ListLeadIn.Restore(ruleCleanedText, Unwrap(reply));
        GuardResult verdict = SmartCleanupGuard.Check(ruleCleanedText, answer, context.CleanupLevel, dictionary);
        return verdict.Accepted
            ? new SmartCleanupResult(answer, true, null)
            : Kept(ruleCleanedText, $"guard: {verdict.Reason}");
    }

    private static SmartCleanupResult Kept(string ruleCleanedText, string reason)
    {
        SimpleFileLogger.LogInfo($"Smart cleanup kept the rule-cleaned text ({reason}).");
        return new SmartCleanupResult(ruleCleanedText, false, reason);
    }

    /// <summary>Takes off what small models wrap their answer in: an empty thinking block, notes they
    /// echoed, the transcript tags they were shown, surrounding whitespace.</summary>
    private static string Unwrap(string? reply)
    {
        string text = ThinkBlockPattern().Replace(reply ?? string.Empty, string.Empty);
        text = NotesBlockPattern().Replace(text, string.Empty);
        text = TranscriptTagPattern().Replace(text, string.Empty);
        return text.Trim();
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlockPattern();

    [GeneratedRegex(@"<notes>.*?</notes>", RegexOptions.Singleline)]
    private static partial Regex NotesBlockPattern();

    [GeneratedRegex(@"</?transcript>")]
    private static partial Regex TranscriptTagPattern();
}
