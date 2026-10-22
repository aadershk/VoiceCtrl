using System.Diagnostics;
using System.IO;
using System.Timers;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Rewrite;
using Timer = System.Timers.Timer;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Owns Smart cleanup at run time (ticket 37): which model runs, when it is loaded and let go, and
/// what happens when it is too slow. Settings and the installed model are read at every Dictation,
/// so a change applies to the next one without a restart. <see cref="Clean"/> never throws: any
/// failure leaves the rule-cleaned text, and a model that keeps timing out is left alone for the
/// rest of the session, since on a slow PC waiting for it every time is worse than not having it.
/// </summary>
public sealed partial class SmartCleanupService : IDisposable
{
    /// <summary>The model is let go after this long without a Dictation, like the speech model.</summary>
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    /// <summary>This many slow or failed calls in a row end Smart cleanup until the next start.</summary>
    private const int MaxConsecutiveFailures = 3;

    private readonly SettingsStore _settings;
    private readonly SmartCleanupPackManager _packs;
    private readonly Func<string, string, ISmartCleanupModel> _createModel;
    private readonly Timer _idleTimer;
    private readonly object _gate = new();

    /// <summary>The longest a Dictation waits for the prime in flight before going ahead without it.</summary>
    private static readonly TimeSpan MaxPrimeWait = TimeSpan.FromSeconds(20);

    private ISmartCleanupModel? _model;
    private string? _loadedFolder;
    private int _consecutiveFailures;
    private bool _disposed;
    private Task _priming = Task.CompletedTask;

    public SmartCleanupService(SettingsStore settings, SmartCleanupPackManager packs, int threads)
        : this(settings, packs, threads, (serverExe, modelPath) => new LlamaServerModel(
            serverExe, modelPath, threads, slotFolder: Path.Combine(Path.GetDirectoryName(modelPath)!, "slots")))
    {
    }

    /// <summary>Test seam: lets the lifecycle be exercised without starting a process.</summary>
    internal SmartCleanupService(
        SettingsStore settings, SmartCleanupPackManager packs, int threads, Func<string, string, ISmartCleanupModel> createModel)
    {
        _settings = settings;
        _packs = packs;
        _createModel = createModel;

        _idleTimer = new Timer(IdleUnloadDelay.TotalMilliseconds) { AutoReset = false };
        _idleTimer.Elapsed += OnIdle;
        _packs.InstalledChanged += Unload;
    }

    /// <summary>True when Smart cleanup is on and a model is on disk.</summary>
    public bool IsAvailable => _settings.Current.Transcription.SmartCleanup && _packs.CurrentFolder() is not null && !GivenUp;

    /// <summary>True when a model is on disk, whatever the Smart cleanup switch says: a rewrite by
    /// voice needs the model, not the switch, which is about Dictations.</summary>
    public bool IsInstalled => _packs.CurrentFolder() is not null;

    private bool GivenUp
    {
        get
        {
            lock (_gate)
            {
                return _consecutiveFailures >= MaxConsecutiveFailures;
            }
        }
    }

    /// <summary>Wait for a transcript of <paramref name="words"/> words: a base plus a share per word,
    /// longer for the Better model, which decodes about half as fast (measured on a Ryzen 7 4800HS,
    /// 8 threads: 22 and 10 tokens a second). Ticket 55 measured the Better model in the real pipeline
    /// at about 1.5 s plus 0.17 s a word with its prompt cached (it reads a transcript at about 20
    /// tokens a second and writes at 9), so 0.2 s a word timed out on lists and 35-word messages; a
    /// timeout costs the whole wait and then pastes the plainer text, so the margin is wider.</summary>
    public static TimeSpan TimeoutFor(SmartCleanupTier tier, int words) => tier == SmartCleanupTier.Standard
        ? SmartCleanupEngine.DefaultTimeout(words)
        : TimeSpan.FromSeconds(Math.Min(20, 2 + 0.3 * words));

    /// <summary>
    /// Wait for a rewrite of a text of <paramref name="words"/> words. A rewrite can write more than
    /// it reads ("turn this into an email" from one line), so the base is a whole short answer at
    /// the decoding speeds above (about 70 words), plus the text's own length as a margin. The
    /// bench's timeouts at 4 + 0.21 s a word cut such answers off (research 39, rewrite-4b).
    /// </summary>
    public static TimeSpan RewriteTimeoutFor(SmartCleanupTier tier, int words)
    {
        (double baseSeconds, double secondsPerWord) = tier == SmartCleanupTier.Standard ? (6.0, 0.12) : (13.0, 0.25);
        return TimeSpan.FromSeconds(Math.Min(60, baseSeconds + secondsPerWord * words));
    }

    /// <summary>
    /// Applies a spoken <paramref name="instruction"/> to <paramref name="text"/> with the local model
    /// (ticket 39). Null when there is no model, it is too slow, it fails or its reply is not a
    /// usable text; the caller then leaves the user's text alone. Never throws, and does not count
    /// toward giving up on Smart cleanup: a long rewrite on a slow PC is not a broken model.
    /// </summary>
    internal async Task<string?> Rewrite(
        string instruction, string text, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        ISmartCleanupModel? model = ModelNow();
        if (model is null)
        {
            return null;
        }

        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        ModelRequest request = new(
            RewriteText.System(dictionary),
            RewriteText.ExamplePairs,
            RewriteText.User(instruction, text),
            MaxTokens: (int)(text.Length / 2.5 * 2) + 160);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RewriteTimeoutFor(_settings.Current.Transcription.SmartCleanupTier, words));

        try
        {
            string? reply = await model.CompleteAsync(request, timeout.Token).ConfigureAwait(false);
            ResetIdleTimer();
            string? rewritten = RewriteText.Clean(reply, text);
            if (rewritten is null)
            {
                SimpleFileLogger.LogInfo("Rewrite: the language model's reply was not usable; the text is unchanged.");
            }

            return rewritten;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SimpleFileLogger.LogInfo("Rewrite: the language model timed out; the text is unchanged.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("Rewrite", ex);
            return null;
        }
    }

    /// <summary>Loads the model and has it read the rewrite rules now, so the wait after the user
    /// stops speaking is only the rewrite. Best-effort: never throws.</summary>
    internal async Task PrimeRewrite(IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        try
        {
            if (ModelNow() is not { } model)
            {
                return;
            }

            ModelRequest request = new(RewriteText.System(dictionary), RewriteText.ExamplePairs, RewriteText.User("fix it", "Hello."), MaxTokens: 1);
            await model.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            ResetIdleTimer();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("RewritePrime", ex);
        }
    }

    /// <summary>Whether Dictations are checked for mis-heard words (ticket 43). A switch for the
    /// benchmarks, which compare the pipeline with and without it.</summary>
    public static bool MendWords { get; set; } = true;

    /// <summary>The wait for the model to list mis-heard words: its reading of the text and a few
    /// short lines, which is far less than a rewrite.</summary>
    private static TimeSpan MendTimeoutFor(int words) => TimeSpan.FromSeconds(Math.Min(6, 2.5 + 0.04 * words));

    /// <summary>
    /// Mends words the speech model got wrong and the sentence gives away ("a table for sex"). Returns
    /// <paramref name="text"/> unchanged for Word for word, short texts, no model, a slow or failing
    /// model, or a reply that is not a short list of plausible pairs. Never throws, and a slow call
    /// does not count toward giving up on Smart cleanup: this step is optional.
    /// </summary>
    internal async Task<string> Mend(
        string text,
        PromptContext context,
        IReadOnlyList<DictionaryEntry> dictionary,
        BuiltInVocabulary vocabulary,
        CancellationToken cancellationToken,
        EngineTrace? trace = null)
    {
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (!MendWords || context.CleanupLevel == CleanupLevels.Verbatim || words < SmartCleanupMender.MinWords || !IsAvailable)
        {
            return text;
        }

        // Only a text holding a word the app has never seen is worth the call: that is where the
        // recogniser's slips show, and a plain sentence of known words has nothing to mend.
        string[] protectedWords = dictionary.SelectMany(entry => entry.AlsoHeardAs.Prepend(entry.Word)).ToArray();
        IReadOnlyList<string> unknown = SmartCleanupMender.UnknownWords(text, vocabulary, protectedWords);
        if (unknown.Count == 0 || ModelNow() is not { } model)
        {
            return text;
        }

        await WaitForPrimeAsync(cancellationToken).ConfigureAwait(false);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MendTimeoutFor(words));
        Stopwatch clock = Stopwatch.StartNew();

        try
        {
            string? reply = await model.CompleteAsync(SmartCleanupMender.Request(text, unknown), timeout.Token).ConfigureAwait(false);
            ResetIdleTimer();
            IReadOnlyList<WordMend> proposed = SmartCleanupMender.Parse(reply);
            List<WordMend> applied = [];
            string mended = SmartCleanupMender.Apply(text, proposed, unknown, vocabulary, protectedWords, applied);
            if (trace is not null)
            {
                trace.Mend = "ran";
                trace.ModelMs += (int)clock.ElapsedMilliseconds;
                trace.MendProposed = proposed.Count;
                trace.MendRefused = proposed.Count - applied.Count;
                trace.MendApplied.AddRange(applied.Select(mend => new WordPair(mend.From, mend.To)));
            }

            return mended;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SimpleFileLogger.LogInfo("Smart cleanup: checking for mis-heard words timed out; the text is unchanged.");
            Note(trace, "timed out", clock);
            return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("SmartCleanupMend", ex);
            Note(trace, "failed", clock);
            return text;
        }
    }

    private static void Note(EngineTrace? trace, string outcome, Stopwatch clock)
    {
        if (trace is not null)
        {
            trace.Mend = outcome;
            trace.ModelMs += (int)clock.ElapsedMilliseconds;
        }
    }

    /// <summary>Edits <paramref name="ruleCleanedText"/> with the local model, or returns it as it was.</summary>
    internal async Task<SmartCleanupResult> Clean(
        string ruleCleanedText,
        PromptContext context,
        string styleNotes,
        IReadOnlyList<DictionaryEntry> dictionary,
        CancellationToken cancellationToken,
        EngineTrace? trace = null)
    {
        SmartCleanupTier tier = _settings.Current.Transcription.SmartCleanupTier;
        ISmartCleanupModel? model = IsAvailable ? ModelNow() : null;
        if (model is not null && SmartCleanupGate.ShouldRun(ruleCleanedText, context))
        {
            await WaitForPrimeAsync(cancellationToken).ConfigureAwait(false);
        }

        SmartCleanupEngine engine = new(() => model, words => TimeoutFor(tier, words));

        Stopwatch clock = Stopwatch.StartNew();
        SmartCleanupResult result = await engine.CleanAsync(ruleCleanedText, context, styleNotes, dictionary, cancellationToken).ConfigureAwait(false);
        trace?.RecordCleanup(result, clock.ElapsedMilliseconds);
        bool slow = result.Reason is { } reason && (reason == "timed out" || reason.StartsWith("model failed", StringComparison.Ordinal));

        if (model is not null)
        {
            lock (_gate)
            {
                _consecutiveFailures = slow ? _consecutiveFailures + 1 : 0;
            }

            if (slow && GivenUp)
            {
                SimpleFileLogger.LogInfo("Smart cleanup is too slow here; it stays off until VoiceCtrl restarts.");
            }

            ResetIdleTimer();
        }

        return result;
    }

    /// <summary>
    /// Loads the model and has it read the rules for this app now, so the wait after the user stops
    /// speaking is only the transcript. Best-effort: never throws, and does nothing when Smart
    /// cleanup is off or not installed.
    /// </summary>
    internal Task PrimeAsync(
        PromptContext context, string styleNotes, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        Task priming = PrimeCoreAsync(context, styleNotes, dictionary, cancellationToken);
        lock (_gate)
        {
            _priming = priming;
        }

        return priming;
    }

    /// <summary>
    /// Waits for the prime in flight, if any (ticket 55). The server answers each request in a slot of
    /// its own: a cleanup sent while the prime is still reading lands in the other slot and reads the
    /// whole prompt cold, which costs far more than finishing the read. The wait comes before the
    /// call's own timeout, since reading the app's notes is not the model being slow.
    /// </summary>
    private async Task WaitForPrimeAsync(CancellationToken cancellationToken)
    {
        Task priming;
        lock (_gate)
        {
            priming = _priming;
        }

        try
        {
            await priming.WaitAsync(MaxPrimeWait, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            SimpleFileLogger.LogInfo("Smart cleanup: the model was still reading its rules; going ahead without waiting longer.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The prime itself was cancelled: nothing left to wait for.
        }
    }

    private async Task PrimeCoreAsync(
        PromptContext context, string styleNotes, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsAvailable || ModelNow() is not { } model)
            {
                return;
            }

            // The same rules, examples and app notes a Dictation will send, with a one-word transcript
            // and room for a single token: the server keeps the processed prefix for the next call.
            ModelRequest request = SmartCleanupPrompt.Request(context, styleNotes, dictionary, "hello", maxTokens: 1) with { KeepInSlot = 0 };
            await model.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

            // The mending rules never change, so the server keeps them for good once read.
            if (MendWords && context.CleanupLevel != CleanupLevels.Verbatim)
            {
                await model.CompleteAsync(SmartCleanupMender.Request("hello", []) with { KeepInSlot = 1 }, cancellationToken).ConfigureAwait(false);
            }

            ResetIdleTimer();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("SmartCleanupPrime", ex);
        }
    }

    /// <summary>The model for the installed tier, started if need be. Null when none is installed.</summary>
    private ISmartCleanupModel? ModelNow()
    {
        string? folder = _packs.CurrentFolder();
        if (folder is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_model is not null && _loadedFolder == folder)
            {
                return _model;
            }

            string? serverExe = SmartCleanupPacks.ServerExe(folder);
            if (serverExe is null)
            {
                SimpleFileLogger.LogInfo("Smart cleanup: the language model server is missing from its folder.");
                return null;
            }

            _model?.Dispose();
            _model = _createModel(serverExe, SmartCleanupPacks.ModelPath(folder));
            _loadedFolder = folder;
            return _model;
        }
    }

    /// <summary>Lets go of the model (ending its process), so its files can be deleted. The next
    /// Dictation starts it again.</summary>
    public void Unload()
    {
        lock (_gate)
        {
            _model?.Dispose();
            _model = null;
            _loadedFolder = null;
            _consecutiveFailures = 0;
        }
    }

    private void ResetIdleTimer()
    {
        if (_disposed)
        {
            return;
        }

        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdle(object? sender, ElapsedEventArgs e)
    {
        Unload();
        SimpleFileLogger.LogInfo("Smart cleanup model unloaded after idle timeout.");
    }

    public void Dispose()
    {
        _disposed = true;
        _packs.InstalledChanged -= Unload;
        _idleTimer.Elapsed -= OnIdle;
        _idleTimer.Dispose();
        Unload();
    }
}
