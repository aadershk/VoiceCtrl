using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Diagnostics;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Updates;
using VoiceCtrl.Dictation;
using VoiceCtrl.Input;

namespace VoiceCtrl.Web;

/// <summary>
/// The rest of the Settings page (slice 23): Sound and Overlay, Privacy (Keep Dictations, Your
/// data, Copy diagnostics) and App (Start with Windows, Automatic updates). Each setting is saved
/// through <see cref="SettingsStore"/>, whose consumers already apply it live; this answers the
/// page and pushes <c>settings.general</c> when anything the rows show changes. Lives as long as
/// the app; the Hub is attached while it is open.
/// </summary>
internal sealed class GeneralSettingsCommands : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly SecretsStore _secrets;
    private readonly DictationHost _dictation;
    private readonly PasteAgainHotkey _pasteAgain;
    private readonly UpdateService _updates;
    private readonly Action _applyStartWithWindows;
    private readonly SynchronizationContext _ui;
    private readonly object _gate = new();
    private SurfaceForm? _hub;
    private bool _pushPending;

    /// <param name="applyStartWithWindows">Makes the Run value match the saved setting (skipped in
    /// the sandbox).</param>
    public GeneralSettingsCommands(
        SettingsStore settings,
        SecretsStore secrets,
        DictationHost dictation,
        PasteAgainHotkey pasteAgain,
        UpdateService updates,
        Action applyStartWithWindows,
        SynchronizationContext ui)
    {
        _settings = settings;
        _secrets = secrets;
        _dictation = dictation;
        _pasteAgain = pasteAgain;
        _updates = updates;
        _applyStartWithWindows = applyStartWithWindows;
        _ui = ui;

        // A drag of the Overlay writes overlay.dock, so the row follows the section, not a cache.
        _settings.DictationChanged += OnSectionChanged;
        _settings.OverlayChanged += OnSectionChanged;
        _settings.PrivacyChanged += OnSectionChanged;
        _settings.AppChanged += OnSectionChanged;
        _updates.Changed += Push;
    }

    public GeneralSettingsView View()
    {
        UserSettings s = _settings.Current;
        return new GeneralSettingsView(
            s.Dictation.SoundCues,
            s.Overlay.Dock,
            s.Overlay.AlwaysShow,
            s.Privacy.KeepDictations,
            s.App.StartWithWindows,
            _updates.View(),
            s.Privacy.EngineRecord);
    }

    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<GetGeneralSettings, GeneralSettingsView>(_ => Task.FromResult(View()));

        dispatcher.Handle<SetSoundCues>(args =>
        {
            _settings.Update(s => s with { Dictation = s.Dictation with { SoundCues = args.On } });
            return Task.CompletedTask;
        });

        dispatcher.Handle<PlaySoundCues>(_ =>
        {
            _dictation.PreviewSoundCues();
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetOverlayDock>(args =>
        {
            RequireDefined(args.Dock, "Unknown Overlay position.");
            _settings.Update(s => s with { Overlay = s.Overlay with { Dock = args.Dock } });
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetAlwaysShowOverlay>(args =>
        {
            _settings.Update(s => s with { Overlay = s.Overlay with { AlwaysShow = args.On } });
            return Task.CompletedTask;
        });

        dispatcher.Handle<PreviewKeepDictations, KeepDictationsImpact>(args =>
        {
            RequireDefined(args.Keep, "Unknown Keep Dictations choice.");
            return Task.FromResult(new KeepDictationsImpact(_dictation.History.WouldRemove(args.Keep)));
        });

        // DictationsCommands applies the section to the history store, which prunes on disk.
        dispatcher.Handle<SetKeepDictations>(args =>
        {
            RequireDefined(args.Keep, "Unknown Keep Dictations choice.");
            _settings.Update(s => s with { Privacy = s.Privacy with { KeepDictations = args.Keep } });
            return Task.CompletedTask;
        });

        // Off deletes the record at once. DictationHost also clears it on any Privacy change, so a
        // settings file edited by hand ends the same way.
        dispatcher.Handle<SetEngineRecord>(args =>
        {
            _settings.Update(s => s with { Privacy = s.Privacy with { EngineRecord = args.On } });
            _dictation.EngineRecorder.ClearIfOff();
            return Task.CompletedTask;
        });

        dispatcher.Handle<OpenDataFolder>(_ =>
        {
            OpenDataFolderInExplorer();
            return Task.CompletedTask;
        });

        dispatcher.Handle<CopyDiagnostics, CopyDiagnosticsResult>(_ => Task.FromResult(new CopyDiagnosticsResult(CopyReport())));

        dispatcher.Handle<SetStartWithWindows>(args =>
        {
            _settings.Update(s => s with { App = s.App with { StartWithWindows = args.On } });
            _applyStartWithWindows();
            return Task.CompletedTask;
        });

        // The check and download report through settings.general; the page doesn't wait for them.
        dispatcher.Handle<CheckForUpdates>(args =>
        {
            _ = _updates.CheckAsync();
            return Task.CompletedTask;
        });

        dispatcher.Handle<DownloadUpdate>(args =>
        {
            _ = _updates.DownloadAsync();
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetAutomaticUpdates>(args =>
        {
            _updates.SetAutomatic(args.On);
            return Task.CompletedTask;
        });

        dispatcher.Handle<RestartToUpdate>(_ =>
        {
            _updates.RestartNow();
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

    private static void RequireDefined<TEnum>(TEnum value, string message)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new BridgeCommandException("bad_args", message);
        }
    }

    private static void OpenDataFolderInExplorer()
    {
        try
        {
            Directory.CreateDirectory(UserDataPaths.Root);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { UserDataPaths.Root }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SimpleFileLogger.LogError("Open data folder", ex);
            throw new BridgeCommandException("open_failed", "The folder couldn't be opened.");
        }
    }

    /// <summary>Builds the scrubbed report and puts it on the clipboard. False when the clipboard
    /// stayed busy.</summary>
    private bool CopyReport()
    {
        var scrubber = new LogScrubber(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName,
            _secrets.AllKeys);

        string report = DiagnosticsReport.Build(new DiagnosticsFacts(
            AppVersion: AppCommands.Version,
            OsDescription: $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            WebView2Version: WebView2Version(),
            Settings: _settings.Current,
            Online: _dictation.Online.EndpointFor(_dictation.Online.Chosen),
            SavedProviderKeys: OnlinePresets.FallbackOrder.Where(p => _secrets.KeyFor(p).Length > 0).ToList(),
            TypeSafeKeySaved: _secrets.TypeSafeApiKey.Length > 0,
            PasteAgainTaken: _pasteAgain.IsTaken,
            DictionaryTerms: _dictation.Personalization.Dictionary.Count,
            Snippets: _dictation.Personalization.Snippets.Snippets.Count,
            Profiles: _dictation.Personalization.Profiles.Count,
            KeptDictations: _dictation.History.Entries.Count,
            LogLines: DiagnosticsReport.LastLines(UserDataPaths.Log),
            CreatedAt: DateTimeOffset.Now,
            EngineRecordLines: _dictation.EngineRecorder.Enabled ? _dictation.EngineRecorder.Lines() : null), scrubber);

        try
        {
            Clipboard.SetDataObject(report, copy: true, retryTimes: 5, retryDelay: 60);
            return true;
        }
        catch (ExternalException ex)
        {
            SimpleFileLogger.LogError("Copy diagnostics", ex);
            return false;
        }
    }

    private static string? WebView2Version()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }

    private void OnSectionChanged<TSection>(TSection section) => Push();

    // Settings and update progress change on other threads. One post is queued at a time and reads
    // the view as it is when it runs, so a burst can't land out of order.
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

            _hub?.Post(new GeneralSettingsChanged(View()));
        }, null);
    }

    public void Dispose()
    {
        _settings.DictationChanged -= OnSectionChanged;
        _settings.OverlayChanged -= OnSectionChanged;
        _settings.PrivacyChanged -= OnSectionChanged;
        _settings.AppChanged -= OnSectionChanged;
        _updates.Changed -= Push;
    }
}
