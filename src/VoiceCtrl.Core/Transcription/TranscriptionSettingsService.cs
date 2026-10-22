using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Status;

namespace VoiceCtrl.Core.Transcription;

/// <summary>What an API key row (an Online provider's, TypeSafe's) can say.</summary>
public enum ApiKeyState
{
    /// <summary>No key: the row offers Add key.</summary>
    None,

    /// <summary>A key is saved and nothing has checked it yet this run.</summary>
    Saved,

    /// <summary>The service accepted the key.</summary>
    Works,

    /// <summary>The service refused the key ("That key didn't work · Try another").</summary>
    Rejected,
}

/// <summary>The Transcription group as the page shows it. A key itself never leaves the host:
/// <see cref="KeyMask"/> is all the page sees of it.</summary>
/// <param name="Provider">The Online provider chosen (ticket 60).</param>
/// <param name="KeyState">The chosen provider's key.</param>
/// <param name="KeyMask">Like "AIza ···· 3f9a"; null when the chosen provider has no key.</param>
/// <param name="Other">The Other provider's address and models, and what its model was found to hear.</param>
/// <param name="SavedKeys">Every provider with a key saved, which a fallback tries after the chosen one.</param>
/// <param name="InstalledPack">The extra language pack Offline uses; None is English only.</param>
/// <param name="DownloadingPack">The pack being downloaded, or whose download stopped.</param>
/// <param name="Download">Progress of <paramref name="DownloadingPack"/>.</param>
/// <param name="Packs">The packs on offer, with the sizes and speeds the row states.</param>
/// <param name="SmartCleanup">Smart cleanup is switched on.</param>
/// <param name="SmartCleanupTier">The model Settings chose.</param>
/// <param name="SmartCleanupInstalled">The model that runs now; null when none is on disk.</param>
/// <param name="SmartCleanupDownloading">The model being downloaded, or whose download stopped.</param>
/// <param name="SmartCleanupDownload">Progress of <paramref name="SmartCleanupDownloading"/>.</param>
/// <param name="SmartCleanupPacks">The models on offer, with their sizes.</param>
public sealed record TranscriptionSettingsView(
    TranscriptionModePreference Mode,
    string DefaultCleanup,
    OnlineProvider Provider,
    ApiKeyState KeyState,
    string? KeyMask,
    OtherProviderSettings Other,
    IReadOnlyList<OnlineProvider> SavedKeys,
    OfflinePack InstalledPack,
    OfflinePack? DownloadingPack,
    ModelDownloadStatus? Download,
    IReadOnlyList<OfflinePackInfo> Packs,
    bool SmartCleanup,
    SmartCleanupTier SmartCleanupTier,
    SmartCleanupTier? SmartCleanupInstalled,
    SmartCleanupTier? SmartCleanupDownloading,
    ModelDownloadStatus? SmartCleanupDownload,
    IReadOnlyList<SmartCleanupPackInfo> SmartCleanupPacks);

/// <summary>
/// The rules behind Settings' Transcription group (ticket 22, providers ticket 60): what may be saved,
/// and when. The bridge commands are thin calls into this. A provider's key is checked against the
/// real service before it is saved, so a typo never replaces a working key; a check that couldn't run
/// (no internet, a busy provider) still saves it, as Setup's "Use it anyway" does. Mode, the provider,
/// Default cleanup and the keys apply live: the Online clients read each of them at every Dictation.
/// </summary>
public sealed class TranscriptionSettingsService : IDisposable
{
    private const int MaxKeyLength = 200;
    private const int MaxAddressLength = 300;
    private const int MaxModelLength = 120;

    private readonly SettingsStore _settings;
    private readonly SecretsStore _secrets;
    private readonly ApiKeyHealth _keyHealth;
    private readonly OfflinePackManager _packs;
    private readonly SmartCleanupPackManager _smartPacks;
    private readonly Func<OnlineEndpoint, string, CancellationToken, Task<KeyCheckOutcome>> _checkKey;
    private readonly OnlineAccess _access;
    private readonly object _gate = new();
    private readonly Dictionary<OnlineProvider, string> _verifiedKeys = [];
    private readonly Dictionary<OnlineProvider, string> _checkedKeys = [];

    /// <param name="checkKey">Checks a key against the provider at the endpoint given.</param>
    /// <param name="geminiModel">Gemini's model, as the advanced settings set it.</param>
    public TranscriptionSettingsService(
        SettingsStore settings,
        SecretsStore secrets,
        ApiKeyHealth keyHealth,
        OfflinePackManager packs,
        SmartCleanupPackManager smartPacks,
        Func<OnlineEndpoint, string, CancellationToken, Task<KeyCheckOutcome>> checkKey,
        string geminiModel = ConfigLoader.DefaultModelId)
    {
        _settings = settings;
        _secrets = secrets;
        _keyHealth = keyHealth;
        _packs = packs;
        _smartPacks = smartPacks;
        _checkKey = checkKey;
        _access = OnlineAccess.From(settings, secrets, keyHealth, geminiModel);

        _settings.TranscriptionChanged += OnTranscriptionChanged;
        _secrets.Changed += RaiseChanged;
        _keyHealth.Changed += RaiseChanged;
        _packs.Changed += RaiseChanged;
        _smartPacks.Changed += RaiseChanged;
    }

    /// <summary>Something the group shows changed. Raised on whatever thread caused it.</summary>
    public event Action? Changed;

    private OnlineProvider Chosen => _access.Chosen;

    public TranscriptionSettingsView View()
    {
        TranscriptionSection section = _settings.Current.Transcription;
        OnlineProvider provider = Chosen;
        string key = _secrets.KeyFor(provider);
        OfflinePackState packs = _packs.State;
        SmartCleanupPackState smart = _smartPacks.State;

        return new TranscriptionSettingsView(
            section.Mode,
            section.DefaultCleanup,
            provider,
            KeyStateOf(provider, key),
            key.Length == 0 ? null : Mask(key),
            section.Other,
            OnlinePresets.FallbackOrder.Where(p => _secrets.KeyFor(p).Length > 0).ToList(),
            packs.Installed,
            packs.Target,
            packs.Download,
            OfflinePackCatalog.All,
            section.SmartCleanup,
            section.SmartCleanupTier,
            smart.Installed,
            smart.Target,
            smart.Download,
            SmartCleanupPacks.All);
    }

    public void SetMode(TranscriptionModePreference mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentException("Unknown Mode.", nameof(mode));
        }

        _settings.Update(s => s with { Transcription = s.Transcription with { Mode = mode } });
    }

    /// <summary>Chooses the Online provider. Each provider keeps its own key, so switching back finds it.</summary>
    public void SetProvider(OnlineProvider provider)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentException("Unknown provider.", nameof(provider));
        }

        _settings.Update(s => s with { Transcription = s.Transcription with { Provider = provider } });
    }

    /// <summary>
    /// Saves the Other provider's address and models. What the model hears is found again by the key
    /// check, which runs now when Other has a key; a check that refuses leaves the entry saved (it is
    /// not a secret) and the row says the key didn't work.
    /// </summary>
    public async Task<SetupKeyCheck?> SetOtherAsync(string baseUrl, string model, string textModel, CancellationToken cancellationToken = default)
    {
        baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
        model = (model ?? "").Trim();
        textModel = (textModel ?? "").Trim();
        if (!IsWebAddress(baseUrl) || baseUrl.Length > MaxAddressLength)
        {
            throw new ArgumentException("The address must start with https:// (or http:// for this PC or your network).", nameof(baseUrl));
        }

        if (model.Length is 0 or > MaxModelLength || textModel.Length > MaxModelLength)
        {
            throw new ArgumentException("The model name is empty or too long.", nameof(model));
        }

        _settings.Update(s => s with { Transcription = s.Transcription with { Other = new OtherProviderSettings(baseUrl, model, AudioInput.Unknown, textModel) } });
        string key = _secrets.KeyFor(OnlineProvider.Other);
        if (key.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            _checkedKeys.Remove(OnlineProvider.Other);
            _verifiedKeys.Remove(OnlineProvider.Other);
        }

        return await CheckAndSaveAsync(OnlineProvider.Other, key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>An address a chat completions request can go to: https, or http on this PC or a
    /// private network (a model server the user runs themselves).</summary>
    public static bool IsWebAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || IsPrivateHost(uri.Host))));

    private static bool IsPrivateHost(string host) =>
        host.StartsWith("192.168.", StringComparison.Ordinal) || host.StartsWith("10.", StringComparison.Ordinal) ||
        host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
        (host.StartsWith("172.", StringComparison.Ordinal) && host.Split('.') is [_, var second, ..] &&
         int.TryParse(second, out int b) && b is >= 16 and <= 31);

    /// <summary>Smart cleanup on or off. Switching it on with no model on disk starts the download
    /// of the chosen one, so the switch does what it says.</summary>
    public async Task SetSmartCleanup(bool on)
    {
        _settings.Update(s => s with { Transcription = s.Transcription with { SmartCleanup = on } });
        if (on && _smartPacks.State is { Installed: null, Target: null })
        {
            await _smartPacks.Pick(_settings.Current.Transcription.SmartCleanupTier).ConfigureAwait(false);
        }
    }

    public void SetDefaultCleanup(string level)
    {
        if (!CleanupLevels.IsValid(level))
        {
            throw new ArgumentException("Unknown cleanup level.", nameof(level));
        }

        _settings.Update(s => s with { Transcription = s.Transcription with { DefaultCleanup = level } });
    }

    /// <summary>
    /// Checks <paramref name="key"/> with the chosen provider, then saves it unless the provider
    /// refused it. Works and NotChecked save (the second, because the key can't be judged offline or
    /// while the provider is busy); DidNotWork leaves the saved key as it was.
    /// </summary>
    public Task<SetupKeyCheck> SaveKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        key = key?.Trim() ?? "";
        if (key.Length is 0 or > MaxKeyLength)
        {
            throw new ArgumentException("The key is empty or too long.", nameof(key));
        }

        OnlineProvider provider = Chosen;
        if (provider == OnlineProvider.Other && !_settings.Current.Transcription.Other.IsComplete())
        {
            throw new ArgumentException("Add the address and the model first.", nameof(key));
        }

        return CheckAndSaveAsync(provider, key, cancellationToken);
    }

    private async Task<SetupKeyCheck> CheckAndSaveAsync(OnlineProvider provider, string key, CancellationToken cancellationToken)
    {
        KeyCheckOutcome outcome = await _checkKey(_access.EndpointFor(provider), key, cancellationToken).ConfigureAwait(false);
        SetupKeyCheck check = SetupFlow.ToKeyCheck(outcome.Result);
        if (check == SetupKeyCheck.DidNotWork)
        {
            if (_secrets.KeyFor(provider) == key)
            {
                _keyHealth.ReportRejected(provider);
            }

            return check;
        }

        lock (_gate)
        {
            _verifiedKeys[provider] = check == SetupKeyCheck.Works ? key : "";
            _checkedKeys[provider] = check == SetupKeyCheck.Works ? key : "";
        }

        if (provider == OnlineProvider.Other && outcome.Audio != AudioInput.Unknown)
        {
            _settings.Update(s => s with { Transcription = s.Transcription with { Other = s.Transcription.Other with { Audio = outcome.Audio } } });
        }

        _secrets.SetKey(provider, key);
        if (check == SetupKeyCheck.Works)
        {
            _keyHealth.ReportAccepted(provider);
        }

        RaiseChanged();
        return check;
    }

    /// <summary>Removes the chosen provider's key; the other providers keep theirs.</summary>
    public void RemoveKey()
    {
        OnlineProvider provider = Chosen;
        _secrets.SetKey(provider, "");
        _keyHealth.ReportAccepted(provider);
    }

    /// <summary>
    /// Checks the chosen provider's saved key once per run, when Settings first shows it, so the row
    /// can say "Works" (or that the key was refused) for a key entered in an earlier run. Does nothing
    /// without a key, or when this key was already checked; a check that couldn't run leaves it "Saved".
    /// </summary>
    public async Task VerifySavedKeyAsync(CancellationToken cancellationToken = default)
    {
        OnlineProvider provider = Chosen;
        string key = _secrets.KeyFor(provider);
        lock (_gate)
        {
            if (key.Length == 0 || _checkedKeys.GetValueOrDefault(provider) == key ||
                (provider == OnlineProvider.Other && !_settings.Current.Transcription.Other.IsComplete()))
            {
                return;
            }

            _checkedKeys[provider] = key;
        }

        KeyCheckOutcome outcome = await _checkKey(_access.EndpointFor(provider), key, cancellationToken).ConfigureAwait(false);
        if (_secrets.KeyFor(provider) != key)
        {
            return;
        }

        switch (outcome.Result)
        {
            case KeyValidation.Valid:
                lock (_gate)
                {
                    _verifiedKeys[provider] = key;
                }

                if (provider == OnlineProvider.Other && outcome.Audio != AudioInput.Unknown)
                {
                    _settings.Update(s => s with { Transcription = s.Transcription with { Other = s.Transcription.Other with { Audio = outcome.Audio } } });
                }

                _keyHealth.ReportAccepted(provider);
                RaiseChanged();
                break;
            case KeyValidation.InvalidKey:
                _keyHealth.ReportRejected(provider);
                break;
            default:
                // Could not be checked; forgotten so the next Settings visit tries again.
                lock (_gate)
                {
                    _checkedKeys.Remove(provider);
                }

                break;
        }
    }

    /// <summary>"AIza ···· 3f9a": the first and last four characters, and only for a key long enough
    /// that they give nothing away. A short one is shown as dots alone.</summary>
    public static string Mask(string key) =>
        key.Length >= 16 ? $"{key[..4]} ···· {key[^4..]}" : "····";

    private ApiKeyState KeyStateOf(OnlineProvider provider, string key)
    {
        if (key.Length == 0)
        {
            return ApiKeyState.None;
        }

        if (_keyHealth.IsRejectedFor(provider))
        {
            return ApiKeyState.Rejected;
        }

        lock (_gate)
        {
            return _verifiedKeys.GetValueOrDefault(provider) == key ? ApiKeyState.Works : ApiKeyState.Saved;
        }
    }

    private void OnTranscriptionChanged(TranscriptionSection section) => RaiseChanged();

    private void RaiseChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _settings.TranscriptionChanged -= OnTranscriptionChanged;
        _secrets.Changed -= RaiseChanged;
        _keyHealth.Changed -= RaiseChanged;
        _packs.Changed -= RaiseChanged;
        _smartPacks.Changed -= RaiseChanged;
    }
}
