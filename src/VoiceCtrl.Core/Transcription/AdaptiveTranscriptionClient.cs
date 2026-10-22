using System.Net.NetworkInformation;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Status;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Wraps the Online providers and the local (offline) model behind one <see cref="ITranscriptionClient"/>,
/// choosing per call from a live <see cref="TranscriptionModeStore"/> preference instead of a fixed
/// startup choice. Online is the chosen provider, then every other provider with a saved key
/// (<see cref="OnlineAccess"/>, ticket 60). Auto falls back to Offline when every provider is busy,
/// unreachable or refuses its key; explicit Online does too, but only when the speech model is
/// already on this PC. Any other API error propagates unchanged, so it stays visible instead of
/// being silently masked as "fell back to offline, still worked."
/// </summary>
public sealed class AdaptiveTranscriptionClient : ITranscriptionClient
{
    /// <summary>Raised whenever Auto mode used the local model because the online attempt was
    /// skipped or failed for connectivity reasons. Purely informational. By the time this fires,
    /// the offline transcription has already succeeded or is about to be attempted regardless.</summary>
    public event Action? FellBackToOffline;

    /// <summary>Long enough for a moment of upstream congestion to clear, short enough that the
    /// user reads the extra wait as the request being slow rather than as the app having hung.</summary>
    private const int OnlineRetryDelayMs = 400;

    private readonly TranscriptionModeStore _modeStore;
    private readonly ITranscriptionClient _onlineClient;
    private readonly ITranscriptionClient _offlineClient;
    private readonly PersonalizationStore _personalization;
    private readonly OnlineAccess _access;
    private readonly Func<bool> _isOfflineReady;
    private readonly SmartCleanupLedger? _ledger;

    /// <summary>
    /// The real thing: each provider's client (Gemini's own, or the OpenAI-compatible one), chained
    /// by <paramref name="access"/>, and the local model.
    /// </summary>
    /// <param name="access">The chosen provider, the keys and the fallback order, read live.</param>
    /// <param name="isOfflineReady">The speech model is on disk, so Online may fall back to it without
    /// a download. Defaults to never.</param>
    /// <param name="cleanupLevel">The default Cleanup Level, read at each Dictation.</param>
    /// <param name="offlineModel">Which Offline model to run, asked before each use.</param>
    /// <param name="pressEnter">The Press Enter setting, read at each Dictation.</param>
    /// <param name="styles">The Style screen's settings, read at each Dictation by both engines.</param>
    /// <param name="smartCleanup">The local language model that edits Offline text. Null for the rules alone.</param>
    /// <param name="ledger">Where the text an engine replaced is left, for the Dictations screen.</param>
    /// <param name="engineRecorder">The opt-in engine record.</param>
    public static AdaptiveTranscriptionClient Create(
        AppConfig config,
        TranscriptionModeStore modeStore,
        OnlineAccess access,
        PersonalizationStore? personalization = null,
        Func<bool>? isOfflineReady = null,
        Func<string>? cleanupLevel = null,
        Func<OfflineModel>? offlineModel = null,
        Func<bool>? pressEnter = null,
        SmartCleanupService? smartCleanup = null,
        SmartCleanupLedger? ledger = null,
        EngineRecorder? engineRecorder = null,
        Func<StyleSettings>? styles = null)
    {
        var local = new LocalTranscriptionClient(
            config,
            shouldStayResident: () => modeStore.Current == TranscriptionModePreference.Offline,
            personalization: personalization,
            modelSource: offlineModel,
            smartCleanup: smartCleanup,
            ledger: ledger,
            cleanupLevel: cleanupLevel,
            pressEnter: pressEnter,
            builtInVocabulary: () => BuiltInVocabulary.Shared,
            engineRecorder: engineRecorder,
            styles: styles);

        ITranscriptionClient ClientFor(OnlineProvider provider) => provider == OnlineProvider.Gemini
            ? new GeminiTranscriptionClient(
                config, personalization: personalization, apiKey: () => access.KeyFor(OnlineProvider.Gemini),
                cleanupLevel: cleanupLevel, pressEnter: pressEnter, ledger: ledger, styles: styles)
            : new CompatibleTranscriptionClient(
                config, () => access.EndpointFor(provider), () => access.KeyFor(provider), local, personalization,
                cleanupLevel, pressEnter, styles, ledger);

        return new AdaptiveTranscriptionClient(
            modeStore, new ChainedTranscriptionClient(access, ClientFor), local, access, personalization, isOfflineReady, ledger);
    }

    /// <summary>Test seam: one online client standing for the chosen provider, whose key is
    /// configured while <paramref name="isApiKeyConfigured"/> says so.</summary>
    internal AdaptiveTranscriptionClient(
        TranscriptionModeStore modeStore,
        ITranscriptionClient onlineClient,
        ITranscriptionClient offlineClient,
        Func<bool> isApiKeyConfigured,
        PersonalizationStore? personalization = null,
        ApiKeyHealth? keyHealth = null,
        Func<bool>? isOfflineReady = null)
        : this(
            modeStore,
            null,
            offlineClient,
            new OnlineAccess(
                () => OnlineProvider.Gemini,
                provider => provider == OnlineProvider.Gemini && isApiKeyConfigured() ? "configured-test-key" : string.Empty,
                health: keyHealth ?? new ApiKeyHealth()),
            personalization,
            isOfflineReady,
            ledger: null,
            singleOnline: onlineClient)
    {
    }

    private AdaptiveTranscriptionClient(
        TranscriptionModeStore modeStore,
        ITranscriptionClient? onlineClient,
        ITranscriptionClient offlineClient,
        OnlineAccess access,
        PersonalizationStore? personalization,
        Func<bool>? isOfflineReady,
        SmartCleanupLedger? ledger,
        ITranscriptionClient? singleOnline = null)
    {
        _modeStore = modeStore;
        _access = access;
        _onlineClient = onlineClient ?? new ChainedTranscriptionClient(access, _ => singleOnline!);
        _offlineClient = offlineClient;
        _personalization = personalization ?? PersonalizationStore.Empty;
        _isOfflineReady = isOfflineReady ?? (() => false);
        _ledger = ledger;
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        // Online, and a Dictation that Smart cleanup leaves alone, must not inherit the last Original.
        _ledger?.Clear();
        string? text = await (_modeStore.Current switch
        {
            TranscriptionModePreference.Offline => _offlineClient.TranscribeAsync(wavBytes, cancellationToken),
            TranscriptionModePreference.Online => TranscribeOnlineAsync(wavBytes, cancellationToken),
            _ => TranscribeAutoAsync(wavBytes, cancellationToken),
        }).ConfigureAwait(false);

        // Applied here, once, rather than inside each client: a snippet is a substitution the user
        // asked for by name, so it has to behave identically whichever path served the audio, and
        // in Auto mode the user does not control which one that was.
        return text is null ? null : _personalization.Snippets.Expand(text);
    }

    /// <summary>
    /// Explicit Online: every provider with a key, then Offline when its speech model is already on
    /// this PC (ticket 60: "then Offline"), never a surprise 600 MB download. Without it, one short
    /// retry recovers the common "high demand" moment, since the user has already spoken the
    /// Dictation and cannot get it back; a real outage then surfaces as an error, not a long hang.
    /// </summary>
    private async Task<string?> TranscribeOnlineAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        try
        {
            return await _onlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OnlineFailures.IsWorthAnotherTry(ex, cancellationToken) && _isOfflineReady())
        {
            SimpleFileLogger.LogInfo($"Online mode: no provider could answer ({OnlineFailures.Describe(ex)}); using Offline");
            FellBackToOffline?.Invoke();
            return await _offlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OnlineServiceException ex) when (OnlineFailures.IsTransient(ex) && !cancellationToken.IsCancellationRequested)
        {
            SimpleFileLogger.LogInfo($"Online mode: transient API error ({OnlineFailures.Describe(ex)}), retrying once");
        }

        await Task.Delay(OnlineRetryDelayMs, cancellationToken).ConfigureAwait(false);
        return await _onlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TranscribeAutoAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        if (!_access.HasAny)
        {
            // Not set up for Online at all. This is "not configured," not a failure, so no toast.
            return await _offlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }

        if (_access.Chain().All(_access.Health.IsRejectedFor))
        {
            // The status card says "Using Offline until it's fixed"; a restart or a new key tries again.
            return await _offlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }

        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            SimpleFileLogger.LogInfo("Auto mode: no network available, falling back to offline");
            FellBackToOffline?.Invoke();
            return await _offlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await _onlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OnlineFailures.IsWorthAnotherTry(ex, cancellationToken))
        {
            // A refused key is not hidden: the status card says so, so this Dictation needn't be lost to it.
            bool keyRefused = ex is OnlineServiceException service && OnlineFailures.IsKeyRejection(service);
            SimpleFileLogger.LogInfo($"Auto mode: no provider could answer ({OnlineFailures.Describe(ex)}), falling back to offline");
            if (!keyRefused)
            {
                FellBackToOffline?.Invoke();
            }

            return await _offlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task PrewarmConnectionAsync(CancellationToken cancellationToken = default)
    {
        switch (_modeStore.Current)
        {
            case TranscriptionModePreference.Offline:
                await _offlineClient.PrewarmConnectionAsync(cancellationToken).ConfigureAwait(false);
                break;

            case TranscriptionModePreference.Online:
                if (_access.HasAny)
                {
                    await _onlineClient.PrewarmConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                break;

            default: // Auto: which branch will actually be needed isn't known yet, so warm both.
                Task offlinePrewarm = _offlineClient.PrewarmConnectionAsync(cancellationToken);
                Task onlinePrewarm = _access.HasAny
                    ? _onlineClient.PrewarmConnectionAsync(cancellationToken)
                    : Task.CompletedTask;
                await Task.WhenAll(offlinePrewarm, onlinePrewarm).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// The local model alone, whatever the Mode, for "Retry offline". It shares this client's
    /// model rather than loading a second copy, and expands Snippets the same way. Disposing it
    /// does nothing; this client owns the model.
    /// </summary>
    public ITranscriptionClient OfflineOnly => new OfflineOnlyClient(this);

    /// <summary>The local speech model as an instruction reader for rewrites, sharing this client's
    /// loaded model. Null where the offline client is a test double.</summary>
    public IInstructionTranscriber? OfflineInstructions => _offlineClient as IInstructionTranscriber;

    /// <summary>Lets go of the loaded Offline model so a language pack's files can be deleted; the
    /// next Offline use loads whichever model is current.</summary>
    public Task UnloadOfflineModelAsync() =>
        _offlineClient is LocalTranscriptionClient local ? local.UnloadAsync() : Task.CompletedTask;

    public void Dispose()
    {
        _onlineClient.Dispose();
        _offlineClient.Dispose();
    }

    private sealed class OfflineOnlyClient(AdaptiveTranscriptionClient owner) : ITranscriptionClient
    {
        public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
        {
            string? text = await owner._offlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
            return text is null ? null : owner._personalization.Snippets.Expand(text);
        }

        public Task PrewarmConnectionAsync(CancellationToken cancellationToken = default) =>
            owner._offlineClient.PrewarmConnectionAsync(cancellationToken);

        public void Dispose()
        {
        }
    }
}
