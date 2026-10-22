using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Online;

/// <summary>
/// One client per provider, made on first use, behind the chain of <see cref="OnlineAccess"/>: the
/// chosen provider, then every other provider with a saved key (ticket 60). Shared by the three
/// Online jobs below.
/// </summary>
internal sealed class ProviderClients<TClient>(OnlineAccess access, Func<OnlineProvider, TClient> create) : IDisposable
    where TClient : class
{
    private readonly Dictionary<OnlineProvider, TClient> _clients = [];
    private readonly object _gate = new();

    public OnlineAccess Access => access;

    public TClient For(OnlineProvider provider)
    {
        lock (_gate)
        {
            if (!_clients.TryGetValue(provider, out TClient? client))
            {
                client = create(provider);
                _clients[provider] = client;
            }

            return client;
        }
    }

    public Task<T> RunAsync<T>(string job, Func<TClient, Task<T>> call, CancellationToken cancellationToken) =>
        access.RunAsync(job, provider => call(For(provider)), cancellationToken);

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (TClient client in _clients.Values)
            {
                (client as IDisposable)?.Dispose();
            }

            _clients.Clear();
        }
    }
}

/// <summary>Online Dictation across the provider chain.</summary>
public sealed class ChainedTranscriptionClient : ITranscriptionClient
{
    private readonly ProviderClients<ITranscriptionClient> _clients;

    public ChainedTranscriptionClient(OnlineAccess access, Func<OnlineProvider, ITranscriptionClient> create)
    {
        _clients = new ProviderClients<ITranscriptionClient>(access, create);
    }

    public Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default) =>
        _clients.RunAsync("Online Dictation", client => client.TranscribeAsync(wavBytes, cancellationToken), cancellationToken);

    /// <summary>Only the provider that will be asked first is warmed.</summary>
    public Task PrewarmConnectionAsync(CancellationToken cancellationToken = default) =>
        _clients.Access.Chain() is [var first, ..] ? _clients.For(first).PrewarmConnectionAsync(cancellationToken) : Task.CompletedTask;

    public void Dispose() => _clients.Dispose();
}

/// <summary>Online rewrite by voice across the provider chain.</summary>
public sealed class ChainedRewriteClient : IRewriteClient, IDisposable
{
    private readonly ProviderClients<IRewriteClient> _clients;

    public ChainedRewriteClient(OnlineAccess access, Func<OnlineProvider, IRewriteClient> create)
    {
        _clients = new ProviderClients<IRewriteClient>(access, create);
    }

    public Task<RewriteOutcome> RewriteAsync(byte[] instructionAudio, string source, CancellationToken cancellationToken, string? heard = null) =>
        _clients.RunAsync("Online rewrite", client => client.RewriteAsync(instructionAudio, source, cancellationToken, heard), cancellationToken);

    public Task<string?> HearAsync(byte[] instructionAudio, CancellationToken cancellationToken) =>
        _clients.RunAsync("Online instruction", client => client.HearAsync(instructionAudio, cancellationToken), cancellationToken);

    public Task PrewarmAsync(CancellationToken cancellationToken = default) =>
        _clients.Access.Chain() is [var first, ..] ? _clients.For(first).PrewarmAsync(cancellationToken) : Task.CompletedTask;

    public void Dispose() => _clients.Dispose();
}

/// <summary>An Email request's body, Online, across the provider chain.</summary>
internal sealed class ChainedEmailBodyWriter : IEmailBodyWriter, IDisposable
{
    private readonly ProviderClients<IEmailBodyWriter> _clients;

    public ChainedEmailBodyWriter(OnlineAccess access, Func<OnlineProvider, IEmailBodyWriter> create)
    {
        _clients = new ProviderClients<IEmailBodyWriter>(access, create);
    }

    public bool IsReady => _clients.Access.HasAny;

    public Task<string?> WriteBodyAsync(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken) =>
        _clients.RunAsync("Online email", client => client.WriteBodyAsync(request, dictionary, cancellationToken), cancellationToken);

    public void Dispose() => _clients.Dispose();
}
