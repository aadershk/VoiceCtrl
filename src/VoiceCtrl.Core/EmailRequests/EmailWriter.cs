using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.EmailRequests;

/// <summary>
/// Writes the email for an Email request (ticket 57): an instruction said while holding the Rewrite
/// key with nothing selected in an email app. Plain dictation never reaches it.
/// </summary>
public interface IEmailWriter
{
    /// <summary>True when the app in front is an email app or site with Email Layout on and Gemini or
    /// the local language model can write now, so a Rewrite key instruction with nothing selected must
    /// be heard first to tell a request from an edit.</summary>
    bool AppliesHere();

    /// <summary>The request in <paramref name="instruction"/> (the Rewrite key's words), with the email
    /// app's Style and the user's name; null when it is an edit of the last Dictation, or writing does
    /// not apply here.</summary>
    EmailRequest? ForInstruction(string instruction);

    /// <summary>The whole email, laid out and in the app's Style, or null when it could not be written
    /// (too slow, a service error, a reply that is not a usable body or adds what the request did not
    /// say). Never throws but for cancellation.</summary>
    Task<string?> WriteAsync(EmailRequest request, CancellationToken cancellationToken);

    /// <summary>Best-effort warm-up while the Rewrite key is held in an email app. Never throws.</summary>
    Task PrewarmAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Gemini or the local model, asked for an email's body. Returns the raw reply.</summary>
internal interface IEmailBodyWriter
{
    /// <summary>Whether this writer can write now (a key, a model on disk).</summary>
    bool IsReady { get; }

    Task<string?> WriteBodyAsync(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken);

    Task PrewarmAsync(IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The real <see cref="IEmailWriter"/>: reads the app in front the way a Dictation does, and chooses
/// Gemini or the local model from the live Mode the way a rewrite by voice does. Offline and Online are
/// taken at their word; Auto goes Online with a working key and a network and falls back to the local
/// model when Gemini refuses the key or can't be reached.
/// </summary>
public sealed class EmailWriter : IEmailWriter, IDisposable
{
    private readonly Func<PromptContext> _context;
    private readonly Func<StyleSettings> _styles;
    private readonly PersonalizationStore _personalization;
    private readonly Func<TranscriptionModePreference> _mode;
    private readonly IEmailBodyWriter _online;
    private readonly IEmailBodyWriter _offline;
    private readonly OnlineAccess _access;
    private readonly Func<bool> _isNetworkAvailable;

    /// <summary>The real thing: the foreground app, the live settings, the Online providers chained by
    /// <paramref name="access"/>, and the Smart cleanup model.</summary>
    public static EmailWriter Create(
        AppConfig config,
        TranscriptionModeStore modeStore,
        PersonalizationStore personalization,
        OnlineAccess access,
        SmartCleanupService smartCleanup,
        Func<string> cleanupLevel,
        Func<StyleSettings> styles)
    {
        PromptContext Context()
        {
            string? process = ForegroundAppDetector.GetForegroundProcessName();
            return GeminiPrompt.ContextFor(
                personalization.Profiles.Resolve(process), process,
                new PromptDefaults(cleanupLevel(), config.EnableToneAwareness, config.PromptStyleApps, config.ChatStyleApps, Styles: styles()),
                ForegroundAppDetector.GetForegroundWindowTitle());
        }

        IEmailBodyWriter WriterFor(OnlineProvider provider) => provider == OnlineProvider.Gemini
            ? new GeminiEmailBodyWriter(config, () => access.KeyFor(OnlineProvider.Gemini))
            : new CompatibleEmailBodyWriter(() => access.EndpointFor(provider), () => access.KeyFor(provider));

        return new EmailWriter(
            Context, styles, personalization, () => modeStore.Current,
            new ChainedEmailBodyWriter(access, WriterFor), new LocalEmailBodyWriter(smartCleanup), access);
    }

    /// <summary>Test and bench seam: one online writer standing for the chosen provider.</summary>
    internal EmailWriter(
        Func<PromptContext> context,
        Func<StyleSettings> styles,
        PersonalizationStore personalization,
        Func<TranscriptionModePreference> mode,
        IEmailBodyWriter online,
        IEmailBodyWriter offline,
        ApiKeyHealth keyHealth,
        Func<bool>? isNetworkAvailable = null)
        : this(context, styles, personalization, mode, online, offline,
            new OnlineAccess(() => OnlineProvider.Gemini, p => p == OnlineProvider.Gemini && online.IsReady ? "configured-test-key" : string.Empty, health: keyHealth),
            isNetworkAvailable)
    {
    }

    private EmailWriter(
        Func<PromptContext> context,
        Func<StyleSettings> styles,
        PersonalizationStore personalization,
        Func<TranscriptionModePreference> mode,
        IEmailBodyWriter online,
        IEmailBodyWriter offline,
        OnlineAccess access,
        Func<bool>? isNetworkAvailable = null)
    {
        _context = context;
        _styles = styles;
        _personalization = personalization;
        _mode = mode;
        _access = access;
        _online = online as ChainedEmailBodyWriter ?? (IEmailBodyWriter)new ChainedEmailBodyWriter(access, _ => online);
        _offline = offline;
        _isNetworkAvailable = isNetworkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
    }

    public bool AppliesHere() => InAnEmailApp(_context()) && Writer() is not null;

    public EmailRequest? ForInstruction(string instruction)
    {
        PromptContext context = _context();
        if (!InAnEmailApp(context) || Writer() is null)
        {
            return null;
        }

        return EmailRequest.FromInstruction(instruction) is { } request
            ? request with { Style = context.Style ?? Style.Formal, YourName = _styles().YourName }
            : null;
    }

    private static bool InAnEmailApp(PromptContext context) => context.Kind == AppKind.Email && context.EmailLayout;

    public async Task<string?> WriteAsync(EmailRequest request, CancellationToken cancellationToken) =>
        (await WriteWithReasonAsync(request, cancellationToken).ConfigureAwait(false)).Email;

    /// <summary><see cref="WriteAsync"/>, plus why no email came back (for the bench): "no body", or
    /// the kind of thing the guard found added, and the body it refused.</summary>
    internal async Task<(string? Email, string? Refused, string? Body)> WriteWithReasonAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<DictionaryEntry> dictionary = _personalization.Dictionary;
        string? reply = await ReplyAsync(request, dictionary, cancellationToken).ConfigureAwait(false);

        string? body = EmailText.Body(reply);
        if (body is null)
        {
            SimpleFileLogger.LogInfo("Email request: no usable body came back; the request is typed as said.");
            return (null, "no body", reply);
        }

        if (EmailGuard.Check(body, request) is { } added)
        {
            // The kind only: the word itself may be private.
            SimpleFileLogger.LogInfo($"Email request: the body added a {added}; the request is typed as said.");
            return (null, added, body);
        }

        string email = EmailText.Assemble(body, request, request.Style, request.YourName);
        return (StyleRules.Apply(email, request.Style, CleanupLevels.Standard, word => BuiltInVocabulary.Shared.IsOrdinaryWord(word)), null, body);
    }

    public async Task PrewarmAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (InAnEmailApp(_context()) && Writer() is { } writer)
            {
                await writer.PrewarmAsync(_personalization.Dictionary, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("EmailRequestPrewarm", ex);
        }
    }

    /// <summary>The writer the Mode calls for now, or null when neither can write.</summary>
    private IEmailBodyWriter? Writer() => _mode() switch
    {
        TranscriptionModePreference.Offline => _offline.IsReady ? _offline : null,
        TranscriptionModePreference.Online => _online.IsReady ? _online : null,
        _ => GoOnline() ? _online : _offline.IsReady ? _offline : null,
    };

    private bool GoOnline() => _online.IsReady && !_access.Chain().All(_access.Health.IsRejectedFor) && _isNetworkAvailable();

    private async Task<string?> ReplyAsync(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        IEmailBodyWriter? writer = Writer();
        if (writer is null)
        {
            return null;
        }

        try
        {
            // The provider chain reports each key's health itself.
            return await writer.WriteBodyAsync(request, dictionary, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ReferenceEquals(writer, _online) && OnlineFailures.IsWorthAnotherTry(ex, cancellationToken))
        {
            return await FallBackAsync(request, dictionary, $"no provider could answer ({OnlineFailures.Describe(ex)})", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("EmailRequest", ex);
            return null;
        }
    }

    /// <summary>In Auto only, the local model writes it when no provider could; Online stays Online.</summary>
    private async Task<string?> FallBackAsync(
        EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, string why, CancellationToken cancellationToken)
    {
        if (_mode() != TranscriptionModePreference.Auto || !_offline.IsReady)
        {
            SimpleFileLogger.LogInfo($"Email request: {why}; the request is typed as said.");
            return null;
        }

        SimpleFileLogger.LogInfo($"Email request, Auto mode: {why}; using the local model.");
        try
        {
            return await _offline.WriteBodyAsync(request, dictionary, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("EmailRequest", ex);
            return null;
        }
    }

    public void Dispose() => (_online as IDisposable)?.Dispose();
}

/// <summary>Offline: the Smart cleanup model writes the body. It needs the model on disk, not the
/// Smart cleanup switch, which is about tidying Dictations (as a rewrite by voice does).</summary>
internal sealed class LocalEmailBodyWriter(SmartCleanupService languageModel) : IEmailBodyWriter
{
    public bool IsReady => languageModel.IsInstalled;

    public Task<string?> WriteBodyAsync(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken) =>
        languageModel.WriteEmailBody(request, dictionary, cancellationToken);

    public Task PrewarmAsync(IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken) =>
        languageModel.PrimeEmail(dictionary, cancellationToken);
}
