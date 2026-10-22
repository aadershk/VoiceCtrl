using System.Runtime.ExceptionServices;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Status;

namespace VoiceCtrl.Core.Online;

/// <summary>
/// The live Online setup: which provider is chosen, each provider's key and endpoint, and the order
/// a fallback walks (ticket 60). Everything is read at each use, so a change in Settings or Setup
/// applies to the next Dictation without a restart.
/// </summary>
public sealed class OnlineAccess
{
    private readonly Func<OnlineProvider> _chosen;
    private readonly Func<OnlineProvider, string> _key;
    private readonly Func<OtherProviderSettings> _other;
    private readonly Func<string> _geminiModel;

    public OnlineAccess(
        Func<OnlineProvider> chosen,
        Func<OnlineProvider, string> key,
        Func<OtherProviderSettings>? other = null,
        ApiKeyHealth? health = null,
        Func<string>? geminiModel = null)
    {
        _chosen = chosen;
        _key = key;
        _other = other ?? (() => new OtherProviderSettings());
        _geminiModel = geminiModel ?? (() => ConfigLoader.DefaultModelId);
        Health = health ?? new ApiKeyHealth(chosen);
    }

    /// <summary>The real thing: the settings store's choice and the secrets store's keys.</summary>
    public static OnlineAccess From(SettingsStore settings, SecretsStore secrets, ApiKeyHealth health, string geminiModel) =>
        new(
            () => settings.Current.Transcription.Provider ?? OnlineProvider.Gemini,
            secrets.KeyFor,
            () => settings.Current.Transcription.Other,
            health,
            () => geminiModel);

    /// <summary>One provider and one key, nothing to fall back to: the benches.</summary>
    public static OnlineAccess Single(OnlineProvider provider, string key, OtherProviderSettings? other = null, string? geminiModel = null) =>
        new(() => provider, p => p == provider ? key : string.Empty, () => other ?? new OtherProviderSettings(),
            geminiModel: geminiModel is null ? null : () => geminiModel);

    public ApiKeyHealth Health { get; }

    public OnlineProvider Chosen => _chosen();

    public string KeyFor(OnlineProvider provider) => _key(provider);

    public OnlineEndpoint EndpointFor(OnlineProvider provider) => OnlinePresets.For(provider, _other(), _geminiModel());

    /// <summary>A key is saved, and for Other an address and a model too.</summary>
    public bool IsUsable(OnlineProvider provider) =>
        AppConfig.IsUsableApiKey(_key(provider)) && (provider != OnlineProvider.Other || _other().IsComplete());

    /// <summary>
    /// Who to ask, in order: the chosen provider, then every other provider with a saved key. One
    /// whose key was refused this run goes last, so a working key answers first, but it is still
    /// tried when nothing else is left (the provider may have taken the key back).
    /// </summary>
    public IReadOnlyList<OnlineProvider> Chain()
    {
        OnlineProvider chosen = _chosen();
        IEnumerable<OnlineProvider> usable = OnlinePresets.FallbackOrder
            .Where(p => p != chosen)
            .Prepend(chosen)
            .Where(IsUsable);
        return usable.OrderBy(p => Health.IsRejectedFor(p) ? 1 : 0).ToList();
    }

    /// <summary>Some provider can be asked.</summary>
    public bool HasAny => OnlinePresets.FallbackOrder.Any(IsUsable);

    /// <summary>
    /// Runs <paramref name="attempt"/> against each provider of <see cref="Chain"/> until one
    /// answers. A provider that is busy, unreachable or refuses its key passes the job on (the key
    /// refusal is reported to <see cref="Health"/>); any other error, and the last provider's error,
    /// is thrown as it was.
    /// </summary>
    public async Task<T> RunAsync<T>(string job, Func<OnlineProvider, Task<T>> attempt, CancellationToken cancellationToken)
    {
        IReadOnlyList<OnlineProvider> chain = Chain();
        if (chain.Count == 0)
        {
            throw new OnlineServiceException(System.Net.HttpStatusCode.Unauthorized, "No Online provider has an API key.");
        }

        for (int i = 0; ; i++)
        {
            OnlineProvider provider = chain[i];
            try
            {
                T result = await attempt(provider).ConfigureAwait(false);
                Health.ReportAccepted(provider);
                if (i > 0)
                {
                    SimpleFileLogger.LogInfo($"{job}: served by {OnlinePresets.NameOf(provider)} after a fallback");
                }

                return result;
            }
            catch (Exception ex) when (OnlineFailures.IsWorthAnotherTry(ex, cancellationToken))
            {
                if (ex is OnlineServiceException service && OnlineFailures.IsKeyRejection(service))
                {
                    Health.ReportRejected(provider);
                }

                bool last = i == chain.Count - 1;
                SimpleFileLogger.LogInfo(
                    $"{job}: {OnlinePresets.NameOf(provider)} failed ({OnlineFailures.Describe(ex)})" +
                    (last ? "" : $", trying {OnlinePresets.NameOf(chain[i + 1])}"));
                if (last)
                {
                    ExceptionDispatchInfo.Throw(ex);
                }
            }
        }
    }
}
