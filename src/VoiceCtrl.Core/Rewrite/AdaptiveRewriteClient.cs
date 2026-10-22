using System.Net.NetworkInformation;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Rewrite;

/// <summary>
/// Chooses the Online providers or the local model for each rewrite from the live Mode, the way the
/// Dictation client does: Offline and Online are taken at their word; Auto goes Online when a working
/// key and a network are there, and falls back to the local model when every provider is busy,
/// unreachable or refuses its key. Any other API error propagates, so it stays visible.
/// </summary>
public sealed class AdaptiveRewriteClient : IRewriteClient, IDisposable
{
    private readonly Func<TranscriptionModePreference> _mode;
    private readonly IRewriteClient _online;
    private readonly IRewriteClient _offline;
    private readonly OnlineAccess _access;
    private readonly Func<bool> _isNetworkAvailable;

    /// <summary>The real thing: each provider's client chained by <paramref name="access"/>, the local
    /// model through <paramref name="smartCleanup"/>, chosen by the live Mode.</summary>
    public static AdaptiveRewriteClient Create(
        AppConfig config,
        TranscriptionModeStore modeStore,
        Personalization.PersonalizationStore personalization,
        OnlineAccess access,
        IInstructionTranscriber instructions,
        SmartCleanupService smartCleanup)
    {
        IRewriteClient ClientFor(OnlineProvider provider) => provider == OnlineProvider.Gemini
            ? new GeminiRewriteClient(config, personalization, () => access.KeyFor(OnlineProvider.Gemini))
            : new CompatibleRewriteClient(() => access.EndpointFor(provider), () => access.KeyFor(provider), instructions, personalization);

        return new AdaptiveRewriteClient(
            () => modeStore.Current, new ChainedRewriteClient(access, ClientFor), new LocalRewriteClient(instructions, smartCleanup, personalization), access);
    }

    /// <summary>Test seam: one online client standing for the chosen provider.</summary>
    internal AdaptiveRewriteClient(
        Func<TranscriptionModePreference> mode,
        IRewriteClient online,
        IRewriteClient offline,
        Func<bool> isApiKeyConfigured,
        ApiKeyHealth keyHealth,
        Func<bool>? isNetworkAvailable = null)
        : this(mode, null, offline, TestAccess(isApiKeyConfigured, keyHealth), isNetworkAvailable, online)
    {
    }

    private AdaptiveRewriteClient(
        Func<TranscriptionModePreference> mode,
        IRewriteClient? online,
        IRewriteClient offline,
        OnlineAccess access,
        Func<bool>? isNetworkAvailable = null,
        IRewriteClient? singleOnline = null)
    {
        _mode = mode;
        _access = access;
        _online = online ?? new ChainedRewriteClient(access, _ => singleOnline!);
        _offline = offline;
        _isNetworkAvailable = isNetworkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
    }

    internal static OnlineAccess TestAccess(Func<bool> isApiKeyConfigured, ApiKeyHealth keyHealth) =>
        new(() => OnlineProvider.Gemini,
            provider => provider == OnlineProvider.Gemini && isApiKeyConfigured() ? "configured-test-key" : string.Empty,
            health: keyHealth);

    public Task<RewriteOutcome> RewriteAsync(byte[] instructionAudio, string source, CancellationToken cancellationToken, string? heard = null) =>
        RouteAsync(client => client.RewriteAsync(instructionAudio, source, cancellationToken, heard), cancellationToken);

    public Task<string?> HearAsync(byte[] instructionAudio, CancellationToken cancellationToken) =>
        RouteAsync(client => client.HearAsync(instructionAudio, cancellationToken), cancellationToken);

    /// <summary>Runs <paramref name="call"/> on the client the Mode calls for.</summary>
    private Task<T> RouteAsync<T>(Func<IRewriteClient, Task<T>> call, CancellationToken cancellationToken) =>
        _mode() switch
        {
            TranscriptionModePreference.Offline => call(_offline),
            TranscriptionModePreference.Online => call(_online),
            _ => AutoAsync(call, cancellationToken),
        };

    private async Task<T> AutoAsync<T>(Func<IRewriteClient, Task<T>> call, CancellationToken cancellationToken)
    {
        if (!_access.HasAny || _access.Chain().All(_access.Health.IsRejectedFor) || !_isNetworkAvailable())
        {
            return await call(_offline).ConfigureAwait(false);
        }

        try
        {
            return await call(_online).ConfigureAwait(false);
        }
        catch (Exception ex) when (OnlineFailures.IsWorthAnotherTry(ex, cancellationToken))
        {
            SimpleFileLogger.LogInfo($"Rewrite, Auto mode: no provider could answer ({OnlineFailures.Describe(ex)}); using the local model.");
        }

        return await call(_offline).ConfigureAwait(false);
    }

    public void Dispose() => (_online as IDisposable)?.Dispose();

    public async Task PrewarmAsync(CancellationToken cancellationToken = default)
    {
        switch (_mode())
        {
            case TranscriptionModePreference.Offline:
                await _offline.PrewarmAsync(cancellationToken).ConfigureAwait(false);
                break;

            case TranscriptionModePreference.Online:
                if (_access.HasAny)
                {
                    await _online.PrewarmAsync(cancellationToken).ConfigureAwait(false);
                }

                break;

            default:
                await Task.WhenAll(
                    _offline.PrewarmAsync(cancellationToken),
                    _access.HasAny ? _online.PrewarmAsync(cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
                break;
        }
    }
}
