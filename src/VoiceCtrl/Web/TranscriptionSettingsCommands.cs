using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Web;

/// <summary>
/// The Settings page's Transcription group (slice 22): the <c>settings.*</c> commands for Mode, the
/// Online provider and its key (ticket 60), Default cleanup and the Offline language packs. The rules live in
/// <see cref="TranscriptionSettingsService"/> and <see cref="OfflinePackManager"/>; this only
/// answers the page and pushes <c>settings.transcription</c> when anything the group shows changes.
/// Lives as long as the app; the Hub is attached while it is open.
/// </summary>
internal sealed class TranscriptionSettingsCommands : IDisposable
{
    private readonly TranscriptionSettingsService _service;
    private readonly OfflinePackManager _packs;
    private readonly SmartCleanupPackManager _smartPacks;
    private readonly SynchronizationContext _ui;
    private SurfaceForm? _hub;
    private readonly object _gate = new();
    private bool _pushPending;

    public TranscriptionSettingsCommands(
        TranscriptionSettingsService service, OfflinePackManager packs, SmartCleanupPackManager smartPacks, SynchronizationContext ui)
    {
        _service = service;
        _packs = packs;
        _smartPacks = smartPacks;
        _ui = ui;
        _service.Changed += Push;
    }

    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<GetTranscriptionSettings, TranscriptionSettingsView>(args =>
        {
            // A key from an earlier run is checked once, so the row can say "Works"; the answer arrives as an event.
            _ = VerifyKeyAsync();
            return Task.FromResult(_service.View());
        });

        dispatcher.Handle<SetMode>(args =>
        {
            Guard(() => _service.SetMode(args.Mode));
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetDefaultCleanup>(args =>
        {
            Guard(() => _service.SetDefaultCleanup(args.Level));
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetProvider>(args =>
        {
            Guard(() => _service.SetProvider(args.Provider));

            // A key saved in an earlier run for this provider is checked, so its row can say "Works".
            _ = VerifyKeyAsync();
            return Task.CompletedTask;
        });

        dispatcher.Handle<SaveOnlineKey, KeyCheckResult>(async args =>
        {
            try
            {
                return new KeyCheckResult(await _service.SaveKeyAsync(args.Key));
            }
            catch (ArgumentException ex)
            {
                throw new BridgeCommandException("bad_args", ex.Message);
            }
        });

        dispatcher.Handle<RemoveOnlineKey>(_ =>
        {
            _service.RemoveKey();
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetOtherProvider, OtherProviderSaved>(async args =>
        {
            try
            {
                return new OtherProviderSaved(await _service.SetOtherAsync(args.BaseUrl, args.Model, args.TextModel));
            }
            catch (ArgumentException ex)
            {
                throw new BridgeCommandException("bad_args", ex.Message);
            }
        });

        dispatcher.Handle<PickOfflinePack>(args =>
        {
            if (!Enum.IsDefined(args.Pack))
            {
                throw new BridgeCommandException("bad_args", "Unknown language pack.");
            }

            // Progress and the end state arrive as settings.transcription events.
            _ = PickAsync(args.Pack);
            return Task.CompletedTask;
        });

        dispatcher.Handle<CancelOfflinePack>(_ =>
        {
            _packs.Cancel();
            return Task.CompletedTask;
        });

        // Smart cleanup (ticket 37). A download outlives the command; progress and the end state
        // arrive as settings.transcription events.
        dispatcher.Handle<SetSmartCleanup>(args =>
        {
            _ = RunAsync("Smart cleanup switch", () => _service.SetSmartCleanup(args.On));
            return Task.CompletedTask;
        });

        dispatcher.Handle<PickSmartCleanup>(args =>
        {
            if (!Enum.IsDefined(args.Tier))
            {
                throw new BridgeCommandException("bad_args", "Unknown Smart cleanup model.");
            }

            _ = RunAsync("Smart cleanup model", () => _smartPacks.Pick(args.Tier));
            return Task.CompletedTask;
        });

        dispatcher.Handle<CancelSmartCleanup>(_ =>
        {
            _smartPacks.Cancel();
            return Task.CompletedTask;
        });

        dispatcher.Handle<RemoveSmartCleanup>(args =>
        {
            _ = RunAsync("Removing Smart cleanup", _smartPacks.Remove);
            return Task.CompletedTask;
        });
    }

    /// <summary>The Hub opened: its page gets this group's events until it closes.</summary>
    public void Attach(SurfaceForm hub)
    {
        _hub = hub;
        hub.FormClosed += (_, _) =>
        {
            if (_hub == hub)
            {
                _hub = null;
            }
        };
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException ex)
        {
            throw new BridgeCommandException("bad_args", ex.Message);
        }
    }

    private static async Task RunAsync(string what, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError(what, ex);
        }
    }

    private async Task PickAsync(OfflinePack pack)
    {
        try
        {
            await _packs.Pick(pack);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Offline language pack", ex);
        }
    }

    private async Task VerifyKeyAsync()
    {
        try
        {
            await _service.VerifySavedKeyAsync();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Online key check", ex);
        }
    }

    // The group's sources change on download and network threads. One post is queued at a time and
    // reads the view as it is when it runs, so a burst can't land out of order.
    private void Push()
    {
        lock (_gate)
        {
            if (_pushPending)
            {
                return;
            }

            _pushPending = true;
        }

        _ui.Post(_ =>
        {
            lock (_gate)
            {
                _pushPending = false;
            }

            _hub?.Post(new TranscriptionSettingsChanged(_service.View()));
        }, null);
    }

    public void Dispose() => _service.Changed -= Push;
}
