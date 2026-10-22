using System.Diagnostics;
using System.Net.Http;
using System.Windows.Forms;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Web;

namespace VoiceCtrl.Setup;

/// <summary>
/// First-run Setup (spec §7): the fixed 900 × 620 window, its <c>setup.*</c> commands and the
/// English model download. The rules for what is saved live in <see cref="SetupFlow"/>; this
/// only wires them to the page and the window.
/// </summary>
internal sealed class SetupWindow : IDisposable
{
    private const int MaxKeyLength = 200;

    private readonly SettingsStore _settings;
    private readonly SecretsStore _secrets;
    private readonly SurfaceForm _form;
    private readonly HttpClient _downloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ModelDownload _download;
    private readonly Action _onFinish;
    private bool _finished;
    private bool _quitting;

    /// <param name="onFinish">Told how the user chose to listen once Setup is saved; the host starts
    /// the Smart cleanup download for Offline from it.</param>
    public SetupWindow(
        WebViewRuntime runtime, SettingsStore settings, SecretsStore secrets, Action<BridgeDispatcher> registerAppCommands, Action onFinish)
    {
        _settings = settings;
        _secrets = secrets;
        _onFinish = onFinish;

        string modelDirectory = LocalModelDownloader.DefaultModelDirectory;
        var clock = Stopwatch.StartNew();
        _download = new ModelDownload(
            (progress, cancellationToken) => LocalModelDownloader.DownloadAllAsync(modelDirectory, _downloadClient, progress, cancellationToken),
            () => LocalModelDownloader.IsFullyDownloaded(modelDirectory),
            () => LocalModelDownloader.BytesOnDisk(modelDirectory),
            LocalModelDownloader.ApproxTotalBytes,
            () => clock.Elapsed);

        _form = new SurfaceForm(runtime, SurfacePage.Setup, dispatcher =>
        {
            registerAppCommands(dispatcher);
            RegisterSetupCommands(dispatcher);
        });
        _form.FormClosed += OnFormClosed;

        // Progress arrives on the download's thread; the page is reached from the UI thread.
        _download.Changed += status =>
        {
            try
            {
                _form.BeginInvoke(() => _form.Post(new ModelDownloadChanged(status)));
            }
            catch (InvalidOperationException)
            {
                // The window is closing or gone; nothing to show it on.
            }
        };
    }

    /// <summary>The Setup window's page host, for the hotkey step's key capture.</summary>
    public SurfaceForm Surface => _form;

    /// <summary>Raised once Setup has gone: <c>true</c> after Open VoiceCtrl (open the Hub),
    /// <c>false</c> after closing early, which counts as skipping. Not raised on Quit.</summary>
    public event Action<bool>? Ended;

    public void Show()
    {
        _form.Show();
        _form.Activate();
    }

    public void Activate()
    {
        if (_form.WindowState == FormWindowState.Minimized)
        {
            _form.WindowState = FormWindowState.Normal;
        }

        _form.Activate();
    }

    /// <summary>The app is quitting: close without saving, so Setup runs again next time.</summary>
    public void CloseForQuit()
    {
        _quitting = true;
        _form.Close();
    }

    /// <summary>What the last key check found an Other model hears, kept for Finish.</summary>
    private AudioInput _otherAudio = AudioInput.Unknown;

    private void RegisterSetupCommands(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<GetSetupState, SetupSnapshot>(_ => Task.FromResult(new SetupSnapshot(
            _settings.Current.Dictation.Hotkeys.HotkeyKey,
            _settings.Current.App.StartWithWindows,
            _download.Status)));

        dispatcher.Handle<CheckKey, KeyCheckResult>(async args =>
        {
            string key = args.Key?.Trim() ?? "";
            if (key.Length is 0 or > MaxKeyLength)
            {
                throw new BridgeCommandException("bad_args", "The key is empty or too long.");
            }

            if (!Enum.IsDefined(args.Provider))
            {
                throw new BridgeCommandException("bad_args", "Unknown provider.");
            }

            // Ticket 60: the chosen provider's own service checks the key; for Other, at the address
            // and model typed, which also finds out whether the model hears audio.
            OtherProviderSettings other = new(args.BaseUrl?.Trim().TrimEnd('/') ?? "", args.Model?.Trim() ?? "");
            if (args.Provider == OnlineProvider.Other && !(other.IsComplete() && TranscriptionSettingsService.IsWebAddress(other.BaseUrl)))
            {
                return new KeyCheckResult(SetupKeyCheck.DidNotWork);
            }

            KeyCheckOutcome outcome = await OnlineKeyValidator.CheckAsync(OnlinePresets.For(args.Provider, other), key);
            if (args.Provider == OnlineProvider.Other)
            {
                _otherAudio = outcome.Audio;
            }

            return new KeyCheckResult(SetupFlow.ToKeyCheck(outcome.Result));
        });

        dispatcher.Handle<DownloadModel>(args =>
        {
            // Progress and the end state arrive as setup.model events.
            _ = _download.Start();
            return Task.CompletedTask;
        });

        dispatcher.Handle<StopModelDownload>(_ =>
        {
            _download.Stop();
            return Task.CompletedTask;
        });

        dispatcher.Handle<FinishSetup>(args =>
        {
            if (!Enum.IsDefined(args.Listening) || !Enum.IsDefined(args.Provider) || args.ApiKey?.Length > MaxKeyLength ||
                args.BaseUrl?.Length > 300 || args.Model?.Length > 120)
            {
                throw new BridgeCommandException("bad_args", "Unknown choice or an overlong key.");
            }

            OtherProviderSettings? other = args.Provider == OnlineProvider.Other
                ? new OtherProviderSettings(args.BaseUrl?.Trim().TrimEnd('/') ?? "", args.Model?.Trim() ?? "", _otherAudio)
                : null;
            SetupFlow.Finish(_settings, _secrets, new SetupChoices(args.Listening, args.ApiKey, args.StartWithWindows, args.SmartCleanup, args.Provider, other));
            _finished = true;
            _onFinish();

            // After the reply is posted, so the page's promise settles before the window goes.
            _form.BeginInvoke(_form.Close);
            return Task.CompletedTask;
        });
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _download.Stop();
        if (_quitting)
        {
            return;
        }

        if (!_finished)
        {
            SetupFlow.Skip(_settings);
        }

        Ended?.Invoke(_finished);
    }

    public void Dispose()
    {
        _download.Stop();
        _form.Dispose();
        _downloadClient.Dispose();
    }
}
