using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Wraps a Gemini (online) and a local (offline) client behind one <see cref="ITranscriptionClient"/>,
/// choosing between them per call from a live <see cref="TranscriptionModeStore"/> preference instead
/// of a fixed startup choice. Auto mode additionally falls back from online to offline when the
/// network looks unavailable, but only for connectivity-shaped failures; a real API-level error
/// (bad request, quota, etc.) propagates unchanged so it stays visible instead of being silently
/// masked as "fell back to offline, still worked."
/// </summary>
public sealed class AdaptiveTranscriptionClient : ITranscriptionClient
{
    /// <summary>Raised whenever Auto mode used the local model because the online attempt was
    /// skipped or failed for connectivity reasons. Purely informational. By the time this fires,
    /// the offline transcription has already succeeded or is about to be attempted regardless.</summary>
    public event Action? FellBackToOffline;

    private readonly TranscriptionModeStore _modeStore;
    private readonly ITranscriptionClient _onlineClient;
    private readonly ITranscriptionClient _offlineClient;
    private readonly bool _isApiKeyConfigured;

    public AdaptiveTranscriptionClient(AppConfig config, TranscriptionModeStore modeStore)
        : this(modeStore, new GeminiTranscriptionClient(config), new LocalTranscriptionClient(config), config.IsApiKeyConfigured)
    {
    }

    /// <summary>Test seam: lets the Auto-mode branching below be exercised against fake clients
    /// instead of the real network/model.</summary>
    internal AdaptiveTranscriptionClient(
        TranscriptionModeStore modeStore,
        ITranscriptionClient onlineClient,
        ITranscriptionClient offlineClient,
        bool isApiKeyConfigured)
    {
        _modeStore = modeStore;
        _onlineClient = onlineClient;
        _offlineClient = offlineClient;
        _isApiKeyConfigured = isApiKeyConfigured;
    }

    public Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default) =>
        _modeStore.Current switch
        {
            TranscriptionModePreference.Offline => _offlineClient.TranscribeAsync(wavBytes, cancellationToken),
            TranscriptionModePreference.Online => _onlineClient.TranscribeAsync(wavBytes, cancellationToken),
            _ => TranscribeAutoAsync(wavBytes, cancellationToken),
        };

    // Status codes that reflect a transient/overloaded condition on Google's side rather than
    // something wrong with the request, so it is worth falling back to offline instead of failing the
    // whole dictation. 429/503 are the confirmed real-world case ("high demand"); 502/504 are
    // included on the same reasoning (upstream gateway hiccup, not a bad request). Deliberately
    // excludes 400/401/403/404, since those mean the request or credentials are wrong, and silently
    // swallowing them into a lower-quality offline result would hide a real problem (e.g. a bad
    // API key) the user needs to see and fix.
    private static bool IsTransient(GeminiApiException ex) => ex.StatusCode is
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.BadGateway or
        HttpStatusCode.GatewayTimeout;

    private async Task<string?> TranscribeAutoAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        if (!_isApiKeyConfigured)
        {
            // Not set up for Online at all. This is "not configured," not a failure, so no toast.
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
            string? result = await _onlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
            SimpleFileLogger.LogInfo("Auto mode: served by online (Gemini)");
            return result;
        }
        catch (GeminiApiException ex) when (IsTransient(ex) && !cancellationToken.IsCancellationRequested)
        {
            SimpleFileLogger.LogInfo($"Auto mode: transient API error ({(int)ex.StatusCode} {ex.StatusCode}), falling back to offline");
            FellBackToOffline?.Invoke();
            return await _offlineClient.TranscribeAsync(wavBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // The caller cancelled, not the network, so let it propagate as a real cancellation
                // instead of masking it as a connectivity fallback.
                throw;
            }

            SimpleFileLogger.LogInfo($"Auto mode: {ex.GetType().Name} talking to Gemini, falling back to offline");
            FellBackToOffline?.Invoke();
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
                if (_isApiKeyConfigured)
                {
                    await _onlineClient.PrewarmConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                break;

            default: // Auto: which branch will actually be needed isn't known yet, so warm both.
                Task offlinePrewarm = _offlineClient.PrewarmConnectionAsync(cancellationToken);
                Task onlinePrewarm = _isApiKeyConfigured
                    ? _onlineClient.PrewarmConnectionAsync(cancellationToken)
                    : Task.CompletedTask;
                await Task.WhenAll(offlinePrewarm, onlinePrewarm).ConfigureAwait(false);
                break;
        }
    }

    public void Dispose()
    {
        _onlineClient.Dispose();
        _offlineClient.Dispose();
    }
}
