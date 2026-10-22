using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Input;

namespace VoiceCtrl.Web;

/// <summary>
/// The Settings page's Dictation group (slice 20): the <c>settings.*</c> commands for the
/// microphone, the three keys and the Press Enter and Mute Other Audio switches (slice 21), the
/// "Press a key" capture, and the <c>mic.*</c> list and live level. Lives as long as the app; the Hub, which comes and goes, is attached while it is open.
/// Everything here runs on the UI thread.
/// </summary>
internal sealed class DictationSettingsCommands : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly MicrophoneCatalog _microphones;
    private readonly InputThread _input;
    private readonly PasteAgainHotkey _pasteAgain;
    private readonly SynchronizationContext _ui;
    private readonly MicrophoneLevelPreview _preview = new();
    private SurfaceForm? _surface;
    private KeyCapture? _capture;
    private TaskCompletionSource<KeyCaptureOutcome>? _captureDone;
    private bool _previewOn;
    private bool _previewing;
    private string? _previewDevice;

    public DictationSettingsCommands(SettingsStore settings, MicrophoneCatalog microphones, InputThread input, PasteAgainHotkey pasteAgain, SynchronizationContext ui)
    {
        _settings = settings;
        _microphones = microphones;
        _input = input;
        _pasteAgain = pasteAgain;
        _ui = ui;

        _settings.DictationChanged += OnDictationChanged;
        _microphones.Changed += OnMicrophonesChanged;
        _input.CaptureKey += OnCaptureKey;
        _preview.LevelChanged += level => _ui.Post(_ => _surface?.Post(new MicLevel(level)), null);
    }

    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<GetDictationSettings, DictationSettingsView>(_ => Task.FromResult(CurrentView()));

        dispatcher.Handle<SetMicrophone>(args =>
        {
            MicrophoneDevice? device = null;
            if (args.Id is not null)
            {
                device = _microphones.Current.Devices.FirstOrDefault(d => d.Id == args.Id)
                    ?? throw new BridgeCommandException("bad_args", "No such microphone.");
            }

            _settings.Update(s => s with { Dictation = s.Dictation with { MicrophoneId = device?.Id, MicrophoneName = device?.Name } });
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetPressEnter>(args =>
        {
            _settings.Update(s => s with { Dictation = s.Dictation with { PressEnter = args.On } });
            return Task.CompletedTask;
        });

        dispatcher.Handle<SetMuteOtherAudio>(args =>
        {
            _settings.Update(s => s with { Dictation = s.Dictation with { MuteOtherAudio = args.On } });
            return Task.CompletedTask;
        });

        dispatcher.Handle<ClearTriggerKey>(_ =>
        {
            _settings.Update(s => s with
            {
                Dictation = s.Dictation with
                {
                    Hotkeys = s.Dictation.Hotkeys with
                    {
                        TriggerEnabled = false,
                        TriggerRemoved = true,
                        TriggerMode = HotkeySettings.TriggerModeSingle,
                        TriggerKey = null,
                        ChordPrefix = null,
                        ChordSecondKeyVk = null,
                        ChordSecondKeyLabel = null,
                        ComboVks = null,
                        ComboLabels = null,
                    },
                },
            });
            return Task.CompletedTask;
        });

        dispatcher.Handle<ClearRewriteKey>(_ =>
        {
            _settings.Update(s => s with
            {
                Dictation = s.Dictation with
                {
                    Hotkeys = s.Dictation.Hotkeys with { RewriteEnabled = false, RewriteVks = null, RewriteLabels = null },
                },
            });
            return Task.CompletedTask;
        });

        dispatcher.Handle<CaptureKey, KeyCaptureOutcome>(args =>
        {
            if (!Enum.IsDefined(args.Target))
            {
                throw new BridgeCommandException("bad_args", "Unknown key.");
            }

            // One row listens at a time; starting another ends the first.
            EndCapture(saved: false, warning: null);

            // Listening swallows every key on the PC, so only ever while the Hub or Setup is in
            // front, where the user can see "Press a key" and Deactivate ends it.
            if (_surface is null || NativeMethods.GetForegroundWindow() != _surface.Handle)
            {
                return Task.FromResult(new KeyCaptureOutcome(Saved: false, Warning: null));
            }

            _capture = new KeyCapture(args.Target, _settings.Current.Dictation);
            _captureDone = new TaskCompletionSource<KeyCaptureOutcome>();
            _input.SetCapturing(true);
            return _captureDone.Task;
        });

        dispatcher.Handle<CancelKeyCapture>(_ =>
        {
            EndCapture(saved: false, warning: null);
            return Task.CompletedTask;
        });

        dispatcher.Handle<GetMicrophones, MicrophonesView>(_ => Task.FromResult(MicrophonesView.From(_microphones.Current)));

        dispatcher.Handle<SetMicPreview>(args =>
        {
            _previewOn = args.On;
            RestartPreview();
            return Task.CompletedTask;
        });
    }

    /// <summary>The Hub (or first-run Setup, whose hotkey step changes keys too) opened: its page
    /// gets this group's events until it closes.</summary>
    public void Attach(SurfaceForm surface)
    {
        _surface = surface;

        // Listening swallows every key, so it must end the moment the surface isn't in front.
        surface.Deactivate += (_, _) => EndCapture(saved: false, warning: null);
        surface.FormClosed += (_, _) =>
        {
            if (_surface != surface)
            {
                return;
            }

            EndCapture(saved: false, warning: null);
            _previewOn = false;
            RestartPreview();
            _surface = null;
        };
    }

    private DictationSettingsView CurrentView() => DictationSettingsView.From(_settings.Current.Dictation, _pasteAgain.IsTaken, HasSeveralInputLanguages());

    /// <summary>Windows only switches the input language with Ctrl + Shift or Alt + Shift when two or
    /// more layouts are installed, so the conflict notice depends on it.</summary>
    private static bool HasSeveralInputLanguages() => VoiceCtrl.Interop.Win32.GetKeyboardLayoutList(0, null) > 1;

    /// <summary>Points the level at the device a Dictation would use now. Reopening a microphone
    /// is slow and blinks Windows' indicator, so it only happens when that device may differ.</summary>
    private void RestartPreview(bool devicesChanged = false)
    {
        if (!_previewOn)
        {
            _preview.Stop();
            _previewing = false;
            return;
        }

        string? device = _microphones.Current.RecordingDevice(_settings.Current.Dictation.MicrophoneId);
        if (_previewing && device == _previewDevice && !devicesChanged)
        {
            return;
        }

        _preview.Start(device);
        _previewing = true;
        _previewDevice = device;
    }

    private void OnCaptureKey(int vk, bool down)
    {
        if (_capture is not { } capture)
        {
            return;
        }

        KeyCaptureStep step = down ? capture.KeyDown(vk) : capture.KeyUp(vk);
        switch (step.Status)
        {
            case KeyCaptureStatus.Hint:
                _surface?.Post(new KeyCaptureHint(step.Hint!));
                break;
            case KeyCaptureStatus.Cancelled:
                EndCapture(saved: false, warning: null);
                break;
            case KeyCaptureStatus.Done:
                Save(capture, step);
                break;
        }
    }

    private void Save(KeyCapture capture, KeyCaptureStep step)
    {
        DictationSection result = step.Result!;
        if (capture.Target == KeyCaptureTarget.PasteAgain && !_pasteAgain.TryRegister(result.PasteAgain))
        {
            // Keep listening: the next combination may be free.
            capture.Reset();
            _surface?.Post(new KeyCaptureHint(KeyCapture.TakenHint(result.PasteAgain)));
            return;
        }

        // Only the row's own field: the rest of the section may have changed since listening began.
        _settings.Update(s => s with
        {
            Dictation = capture.Target == KeyCaptureTarget.PasteAgain
                ? s.Dictation with { PasteAgain = result.PasteAgain }
                : s.Dictation with { Hotkeys = result.Hotkeys },
        });
        EndCapture(saved: true, step.Warning);
    }

    private void EndCapture(bool saved, string? warning)
    {
        if (_capture is null)
        {
            return;
        }

        _capture = null;
        _input.SetCapturing(false);
        _captureDone?.TrySetResult(new KeyCaptureOutcome(saved, warning));
        _captureDone = null;
    }

    // Raised on the thread that saved: the UI thread for every Dictation-section change.
    private void OnDictationChanged(DictationSection section) =>
        _ui.Post(_ =>
        {
            _surface?.Post(new DictationSettingsChanged(CurrentView()));
            RestartPreview();
        }, null);

    private void OnMicrophonesChanged() =>
        _ui.Post(_ =>
        {
            _surface?.Post(new MicrophonesChanged(MicrophonesView.From(_microphones.Current)));
            RestartPreview(devicesChanged: true);
        }, null);

    public void Dispose()
    {
        _settings.DictationChanged -= OnDictationChanged;
        _microphones.Changed -= OnMicrophonesChanged;
        _input.CaptureKey -= OnCaptureKey;
        EndCapture(saved: false, warning: null);
        _preview.Dispose();
    }
}
