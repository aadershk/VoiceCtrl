using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Dictation;
using VoiceCtrl.Input;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Web;

/// <summary>
/// The Dictations screen (slice 17): the <c>dictations.*</c> commands, and
/// <c>dictations.changed</c> whenever what the screen shows changes. Lives as long as the app;
/// the Hub is attached while it is open. Everything here runs on the UI thread.
/// </summary>
internal sealed class DictationsCommands : IDisposable
{
    /// <summary>How long to wait for the window we brought back to really be in front before pasting.</summary>
    private static readonly TimeSpan FocusWait = TimeSpan.FromMilliseconds(500);

    private readonly DictationHost _dictation;
    private readonly SettingsStore _settings;
    private readonly LastAppWindow _lastApp;
    private readonly SynchronizationContext _ui;
    private SurfaceForm? _hub;
    private bool _pushPending;

    public DictationsCommands(DictationHost dictation, SettingsStore settings, LastAppWindow lastApp, SynchronizationContext ui)
    {
        _dictation = dictation;
        _settings = settings;
        _lastApp = lastApp;
        _ui = ui;

        _dictation.History.Changed += Push;
        _dictation.Controller.FailedDictationsChanged += Push;
        _settings.PrivacyChanged += OnPrivacyChanged;
        _settings.DictationChanged += OnDictationChanged;
    }

    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<GetDictations, DictationsView>(_ => Task.FromResult(CurrentView()));

        dispatcher.Handle<PasteDictationAgain, HubPasteResult>(args => PasteAgainAsync(args.Id));

        dispatcher.Handle<CopyDictation>(args =>
        {
            DictationRecord record = Find(args.Id) ?? throw new BridgeCommandException("not_found", "No such Dictation.");
            Copy(record.Text);
            return Task.CompletedTask;
        });

        dispatcher.Handle<CopyAllDictations, CopyAllResult>(_ =>
        {
            IReadOnlyList<DictationRecord> all = _dictation.History.Entries;
            if (all.Count > 0)
            {
                Copy(string.Join(Environment.NewLine + Environment.NewLine, all.Select(r => r.Text)));
            }

            return Task.FromResult(new CopyAllResult(all.Count));
        });

        // Smart cleanup's edit undone for one row. If it is the newest, Paste Again follows it, so the
        // text the Latest hero shows is the text the key pastes.
        dispatcher.Handle<RestoreDictationOriginal>(args =>
        {
            bool wasNewest = _dictation.History.Entries.FirstOrDefault()?.Id == args.Id;
            if (_dictation.History.RestoreOriginal(args.Id) && wasNewest && _dictation.History.Entries.FirstOrDefault() is { } restored)
            {
                _dictation.LastTranscription.Set(restored.Text);
            }

            return Task.CompletedTask;
        });

        dispatcher.Handle<DeleteDictation>(args =>
        {
            _dictation.History.Remove(args.Id);
            return Task.CompletedTask;
        });

        dispatcher.Handle<UndoDeleteDictation>(args =>
        {
            _dictation.History.Undo(args.Id);
            return Task.CompletedTask;
        });

        // "Clear all" forgets the latest too, so Paste Again can't bring back what was just cleared.
        dispatcher.Handle<ClearAllDictations>(_ =>
        {
            _dictation.History.Clear();
            _dictation.LastTranscription.Clear();
            return Task.CompletedTask;
        });

        dispatcher.Handle<RetryDictation, RetryResult>(async args =>
            new RetryResult(await _dictation.Controller.RetryFromDictations(args.Id, args.Offline)));

        dispatcher.Handle<DismissFailedDictation>(args =>
        {
            _dictation.Controller.DismissFailed(args.Id);
            return Task.CompletedTask;
        });
    }

    /// <summary>The Hub opened: its page gets <c>dictations.changed</c> until it closes.</summary>
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

    private DictationsView CurrentView() => DictationsView.From(
        _settings.Current.Privacy.KeepDictations,
        _dictation.History.Entries,
        _dictation.Controller.FailedDictations,
        _dictation.Controller.CanRetryOffline,
        _settings.Current.Dictation.PasteAgain.Chips);

    private DictationRecord? Find(Guid id) => _dictation.History.Entries.FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// Brings back the window that was in front before the Hub and pastes there (spec §7). The Hub
    /// is in front when this runs, so Windows lets this process hand the foreground on. Without
    /// such a window, the text is copied instead and the page says where it is.
    /// </summary>
    private async Task<HubPasteResult> PasteAgainAsync(Guid? id)
    {
        string? text = id is { } recordId ? Find(recordId)?.Text : _dictation.LastTranscription.Text;
        if (text is null)
        {
            return new HubPasteResult(HubPasteOutcome.Busy, null);
        }

        IntPtr target = _lastApp.Window;
        if (target == IntPtr.Zero || !await BringToFrontAsync(target))
        {
            Copy(text);
            return new HubPasteResult(HubPasteOutcome.NoWindow, null);
        }

        string? app = LastAppWindow.AppName(target);
        InjectionResult? result = await _dictation.Controller.PasteAgain(text);
        HubPasteOutcome outcome = result switch
        {
            InjectionResult.Injected => HubPasteOutcome.Pasted,
            InjectionResult.ClipboardOnlyElevatedTarget => HubPasteOutcome.Copied,
            InjectionResult.Failed => HubPasteOutcome.Failed,
            _ => HubPasteOutcome.Busy,
        };
        return new HubPasteResult(outcome, app);
    }

    private static async Task<bool> BringToFrontAsync(IntPtr window)
    {
        if (Win32.IsIconic(window))
        {
            Win32.ShowWindow(window, Win32.SW_RESTORE);
        }

        Win32.SetForegroundWindow(window);
        DateTime giveUpAt = DateTime.UtcNow + FocusWait;
        while (NativeMethods.GetForegroundWindow() != window)
        {
            if (DateTime.UtcNow > giveUpAt)
            {
                SimpleFileLogger.LogInfo("Paste Again from the Hub: the last app didn't come to the front");
                return false;
            }

            await Task.Delay(20);
        }

        return true;
    }

    /// <summary>Sets the clipboard, retrying while another app holds it open.</summary>
    private static void Copy(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 60);
        }
        catch (ExternalException ex)
        {
            SimpleFileLogger.LogError("Copy", ex);
            throw new BridgeCommandException("copy_failed", "The clipboard is busy.");
        }
    }

    private void OnPrivacyChanged(PrivacySection section) =>
        _ui.Post(_ =>
        {
            // The subtitle and "not kept" state follow Keep Dictations even when nothing was pruned.
            _dictation.History.Keep = section.KeepDictations;
            if (section.KeepDictations == KeepDictations.None)
            {
                _dictation.Controller.DropCancelledRecordings();
            }

            Push();
        }, null);

    private void OnDictationChanged(DictationSection section) => Push();

    /// <summary>One post per burst: a retry changes both the failures and the history at once.</summary>
    private void Push()
    {
        if (_pushPending)
        {
            return;
        }

        _pushPending = true;
        _ui.Post(_ =>
        {
            _pushPending = false;
            _hub?.Post(new DictationsChanged(CurrentView()));
        }, null);
    }

    public void Dispose()
    {
        _dictation.History.Changed -= Push;
        _dictation.Controller.FailedDictationsChanged -= Push;
        _settings.PrivacyChanged -= OnPrivacyChanged;
        _settings.DictationChanged -= OnDictationChanged;
    }
}
