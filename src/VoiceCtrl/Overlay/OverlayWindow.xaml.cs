using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Overlay;

public partial class OverlayWindow : Window
{
    // Edge Sliver palette (ticket 17), replacing the old "Pulse Pill" colors.
    private static readonly SolidColorBrush IdleBrush = new(Color.FromRgb(0x8D, 0x93, 0xA3));
    private static readonly SolidColorBrush RecordingBrush = new(Color.FromRgb(0xF0, 0x47, 0x2B));
    private static readonly SolidColorBrush ProcessingBrush = new(Color.FromRgb(0xD9, 0xA4, 0x41));
    private static readonly SolidColorBrush RecordingBarsBrush = new(Color.FromRgb(0xF0, 0xA9, 0x99));
    private static readonly SolidColorBrush ProcessingBarsBrush = new(Color.FromArgb(0x99, 0xD9, 0xA4, 0x41));

    /// <summary>The sliver's min-width and padding at idle vs. Recording/Processing (ticket 17's
    /// "widens 84px→128px on record"). Padding drives the widen, MinWidth is the floor once the
    /// bars/mic content alone wouldn't reach it.</summary>
    private const double SliverMinWidthIdle = 84;
    private const double SliverMinWidthActive = 128;
    private static readonly Thickness SliverPaddingIdle = new(14, 7, 14, 5);
    private static readonly Thickness SliverPaddingActive = new(18, 9, 18, 7);

    /// <summary>The 2px lift off the screen edge on Recording/Processing (ticket 17's answer).</summary>
    private const double SliverLiftActive = -2;

    /// <summary>Bar height at rest, and how much level growth can add on top, mirroring the
    /// halo's <see cref="MaxLevelScale"/> growth but expressed as a height delta instead of a
    /// scale factor. Per-bar weights give the four bars a staggered, waveform-like look rather
    /// than moving in perfect lockstep, since only one aggregate level value is available.</summary>
    private const double BarBaseHeight = 4;
    private const double BarMaxGrowth = 10;
    private static readonly double[] BarWeights = [0.6, 1.0, 0.8, 0.5];

    /// <summary>How far the level halo grows at full scale. Bounded by the window: the mic dot is
    /// 16px, so anything much past this reads as a soft glow rather than a second circle.</summary>
    private const double MaxLevelScale = 1.35;

    /// <summary>Share of the gap closed per update on the way down. The meter rises instantly so a
    /// sudden word registers, and falls over a few frames so it doesn't strobe between syllables.</summary>
    private const double LevelDecay = 0.35;

    private readonly DictationStateMachine _state = new();
    private readonly WasapiAudioRecorder _recorder = new();
    private readonly AppConfig _config;
    private readonly ITranscriptionClient _transcriptionClient;
    private readonly TranscriptionModeStore _modeStore;
    private readonly ITextInjector _textInjector;
    private readonly LastTranscriptionStore _lastTranscription;
    private readonly DictationHistoryStore _history;

    private double _smoothedLevel;
    private float _pendingLevel;
    private int _isLevelUpdateQueued;

    public OverlayWindow(
        AppConfig config,
        ITranscriptionClient transcriptionClient,
        TranscriptionModeStore modeStore,
        ITextInjector textInjector,
        LastTranscriptionStore lastTranscription,
        DictationHistoryStore history)
    {
        _lastTranscription = lastTranscription;
        _history = history;
        InitializeComponent();
        _config = config;
        _transcriptionClient = transcriptionClient;
        _modeStore = modeStore;
        _textInjector = textInjector;
        _recorder.LevelChanged += OnRecorderLevelChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        // ShowActivated="False" only suppresses activation on the first Show(). It does not
        // stop a later click from stealing focus. These two extended styles plus the
        // WM_MOUSEACTIVATE hook below are what actually keep focus on the target app.
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// The mic-click entry point. Only reachable while the bar is already visible (double-tap is
    /// what shows it), so Idle here means "click to start" and Recording means "click to stop" —
    /// visibility itself is never touched from this path; see <see cref="ToggleBarVisibility"/>.
    /// </summary>
    public void ToggleRecording()
    {
        switch (_state.RequestToggle())
        {
            case DictationAction.Start:
                _ = RunStartAsync();
                break;
            case DictationAction.Stop:
                _ = RunStopAsync(hideWhenDone: false);
                break;
        }
    }

    /// <summary>
    /// The double-tap-hotkey entry point. Visibility is otherwise never changed by anything else
    /// in this class: a successful paste and an error clearing both settle back to an
    /// idle-but-visible bar, so this is the only gesture that makes it disappear. The one
    /// exception is a double-tap that lands mid-recording — there is no mic left to click once
    /// the bar is hidden, so it finishes the recording (stop/transcribe/paste) before hiding
    /// rather than silently discarding what was said.
    /// </summary>
    public void ToggleBarVisibility()
    {
        if (!IsVisible)
        {
            ShowBar();
            return;
        }

        if (_state.IsRecording && _state.RequestToggle() == DictationAction.Stop)
        {
            _ = RunStopAsync(hideWhenDone: true);
            return;
        }

        Hide();
    }

    /// <summary>
    /// The Trigger Key's Tap entry point: does exactly what <see cref="ToggleRecording"/> does,
    /// except it no-ops if the bar is hidden rather than assuming it is already visible — unlike a
    /// mouse click on the mic, a Tap can land at any time, per ticket 03's answer ("No-op if the
    /// bar is hidden").
    /// </summary>
    public void TriggerTap()
    {
        if (!IsVisible)
        {
            return;
        }

        ToggleRecording();
    }

    /// <summary>
    /// The Trigger Key's Hold-start entry point: begins a Dictation immediately from any state,
    /// skipping the double-tap-to-show step entirely, per ticket 03's answer. Shows the bar first
    /// if it was hidden, so there is something on screen once <see cref="RunStartAsync"/> flips the
    /// mic to recording.
    /// </summary>
    public void StartHandsFreeRecording()
    {
        if (!_state.RequestStart())
        {
            return;
        }

        if (!IsVisible)
        {
            ShowBar();
        }

        _ = RunStartAsync();
    }

    /// <summary>
    /// The Trigger Key's Hold-release entry point: stops, transcribes, pastes, and hides the bar —
    /// the same "finish then hide" path as a double-tap landing mid-recording (see
    /// <see cref="ToggleBarVisibility"/>), since Hold-to-Talk has no separate cancel gesture either.
    /// </summary>
    public void StopHandsFreeRecording()
    {
        if (_state.IsRecording && _state.RequestToggle() == DictationAction.Stop)
        {
            _ = RunStopAsync(hideWhenDone: true);
        }
    }

    // AdaptiveTranscriptionClient.FellBackToOffline can fire from a background thread (raised after
    // an awaited network call fails), so this dispatches explicitly rather than touching StatusText
    // directly. Skips _state/UpdateVisualState on purpose, since routing through SetError would flip
    // the mic ellipse back to idle-grey while the local model is still actually transcribing.
    public void ShowFellBackToOfflineNotice()
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = "Using offline mode";
            StatusText.Visibility = Visibility.Visible;
        });
    }

    private async Task RunStartAsync()
    {
        try
        {
            try
            {
                // Before the window work, not after: the user is already speaking by the time the
                // second tap of the double-tap lands, so every millisecond spent on layout before
                // the capture device is live is a millisecond clipped off the front of the clip.
                _recorder.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceCtrl] Failed to start recording: {ex}");
                SimpleFileLogger.LogError("StartRecording", ex);
                await ShowTransientMessageAsync("Microphone unavailable").ConfigureAwait(true);
                return;
            }

            // Which client(s) actually need warming now depends on the live mode preference, not a
            // fixed startup choice. AdaptiveTranscriptionClient decides that internally.
            _ = _transcriptionClient.PrewarmConnectionAsync();

            // Only reachable via a click on an already-visible bar, so there is no Show() here —
            // just flip the mic to red.
            _state.SetRecording();
            UpdateVisualState();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("StartDictation", ex);
            _state.Reset();
            UpdateVisualState();
        }
        finally
        {
            _state.EndTransition();
        }
    }

    private async Task RunStopAsync(bool hideWhenDone)
    {
        try
        {
            _state.SetProcessing();
            UpdateVisualState();

            await RunStopPipelineAsync(hideWhenDone).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // RunStopPipelineAsync handles every failure it expects. Anything reaching here is
            // unforeseen, and swallowing it silently would leave the bar stuck on amber forever.
            SimpleFileLogger.LogError("StopDictation", ex);
            _state.Reset();
            UpdateVisualState();
            if (hideWhenDone)
            {
                Hide();
            }
        }
        finally
        {
            _state.EndTransition();
        }
    }

    /// <summary>
    /// Raised on the WASAPI capture thread. Coalescing rather than posting every sample keeps a
    /// busy dispatcher from accumulating a backlog of stale level updates that would then play
    /// back as a laggy meter: the newest value always wins, and at most one post is ever pending.
    /// </summary>
    private void OnRecorderLevelChanged(float level)
    {
        _pendingLevel = level;

        if (Interlocked.Exchange(ref _isLevelUpdateQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Interlocked.Exchange(ref _isLevelUpdateQueued, 0);
            ApplyLevel(_pendingLevel);
        });
    }

    private void ApplyLevel(float level)
    {
        if (_state.State != DictationState.Recording)
        {
            ResetLevel();
            return;
        }

        _smoothedLevel = level > _smoothedLevel
            ? level
            : _smoothedLevel + ((level - _smoothedLevel) * LevelDecay);

        double scale = 1.0 + (_smoothedLevel * (MaxLevelScale - 1.0));
        LevelScale.ScaleX = scale;
        LevelScale.ScaleY = scale;

        Bar1.Height = BarHeight(0);
        Bar2.Height = BarHeight(1);
        Bar3.Height = BarHeight(2);
        Bar4.Height = BarHeight(3);
    }

    private double BarHeight(int index) => BarBaseHeight + (_smoothedLevel * BarMaxGrowth * BarWeights[index]);

    private void ResetLevel()
    {
        _smoothedLevel = 0;
        LevelScale.ScaleX = 1.0;
        LevelScale.ScaleY = 1.0;
        Bar1.Height = Bar2.Height = Bar3.Height = Bar4.Height = BarBaseHeight;
    }

    private void ShowBar()
    {
        UpdateVisualState();

        // Width/Height are fixed in XAML (not SizeToContent), so the final position is known
        // before Show(), which avoids a show-then-jump flicker from positioning after layout.
        // bottomMarginDip: 0 is what makes this the Edge Sliver (ticket 17) rather than the old
        // floating capsule — the window's bottom edge, and so the sliver's bottom edge, sits
        // exactly on the work-area edge instead of hovering above it.
        Point pos = MonitorPositioner.GetBottomCenterPosition(Width, Height, bottomMarginDip: 0);
        Left = pos.X;
        Top = pos.Y;

        Show();
    }

    private void SliverBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ToggleRecording();
    }

    private async Task RunStopPipelineAsync(bool hideWhenDone)
    {
        var pipelineStopwatch = System.Diagnostics.Stopwatch.StartNew();

        AudioClip clip;
        try
        {
            clip = await _recorder.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceCtrl] Failed to stop recording: {ex}");
            SimpleFileLogger.LogError("StopRecording", ex);
            await ShowTransientMessageAsync("Recording failed", hideWhenDone).ConfigureAwait(true);
            return;
        }

        long stopElapsedMs = pipelineStopwatch.ElapsedMilliseconds;

        if (clip.IsLikelySilent())
        {
            await ShowTransientMessageAsync("No speech detected", hideWhenDone).ConfigureAwait(true);
            return;
        }

        if (_modeStore.Current == TranscriptionModePreference.Online && !_config.IsApiKeyConfigured)
        {
            await ShowTransientMessageAsync("Add your Gemini API key in .env", hideWhenDone).ConfigureAwait(true);
            return;
        }

        string? text;
        try
        {
            text = await _transcriptionClient.TranscribeAsync(clip.WavBytes).ConfigureAwait(true);
        }
        catch (TranscriptionException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceCtrl] Transcription error: {ex}");
            SimpleFileLogger.LogError("Transcription", ex);
            SimpleFileLogger.LogInfo($"Pipeline failed at transcribe after {pipelineStopwatch.ElapsedMilliseconds - stopElapsedMs}ms");
            await ShowTransientMessageAsync("Transcription failed", hideWhenDone).ConfigureAwait(true);
            return;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceCtrl] Network error: {ex}");
            SimpleFileLogger.LogError("Network", ex);
            SimpleFileLogger.LogInfo($"Pipeline failed at transcribe after {pipelineStopwatch.ElapsedMilliseconds - stopElapsedMs}ms");
            await ShowTransientMessageAsync("No internet connection", hideWhenDone).ConfigureAwait(true);
            return;
        }
        catch (TaskCanceledException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceCtrl] Request timed out: {ex}");
            SimpleFileLogger.LogError("Timeout", ex);
            SimpleFileLogger.LogInfo($"Pipeline failed at transcribe after {pipelineStopwatch.ElapsedMilliseconds - stopElapsedMs}ms");
            await ShowTransientMessageAsync("Request timed out", hideWhenDone).ConfigureAwait(true);
            return;
        }

        long transcribeElapsedMs = pipelineStopwatch.ElapsedMilliseconds;

        if (text is null)
        {
            await ShowTransientMessageAsync("No speech detected", hideWhenDone).ConfigureAwait(true);
            return;
        }

        // Recorded before the injection is attempted, not after it succeeds: the case worth
        // covering is precisely the one where the paste does not land where the user expected.
        _lastTranscription.Set(text);
        _history.Add(text);

        InjectionResult result = await _textInjector.InjectAsync(text).ConfigureAwait(true);
        long injectElapsedMs = pipelineStopwatch.ElapsedMilliseconds;

        SimpleFileLogger.LogInfo(
            $"Pipeline timing: mode={_modeStore.Current} stop={stopElapsedMs}ms transcribe={transcribeElapsedMs - stopElapsedMs}ms " +
            $"inject={injectElapsedMs - transcribeElapsedMs}ms total={injectElapsedMs}ms");

        switch (result)
        {
            case InjectionResult.ClipboardOnlyElevatedTarget:
                await ShowTransientMessageAsync("Copied. Press Ctrl+V (elevated window)", hideWhenDone).ConfigureAwait(true);
                break;

            case InjectionResult.Failed:
                // Names the recovery rather than just reporting the failure, since the whole point
                // of keeping the text is that the user knows where to go and get it.
                await ShowTransientMessageAsync("Paste failed. Tray: Copy last transcription", hideWhenDone).ConfigureAwait(true);
                break;

            default:
                _state.Reset();
                UpdateVisualState();
                if (hideWhenDone)
                {
                    Hide();
                }
                break;
        }
    }

    /// <summary>
    /// Shows a message on the bar, then clears it back to idle. The bar itself is already visible
    /// by the time this runs (only reachable mid-pipeline), so no Show() is needed — only
    /// <paramref name="hideWhenDone"/> callers (the double-tap-mid-recording path) hide it once
    /// the message clears. Callers must already hold the state machine's transition gate for the
    /// whole call: the clear runs after an await, and if a new recording were allowed to start in
    /// the meantime, this method's Reset() would stomp on it.
    /// </summary>
    private async Task ShowTransientMessageAsync(string message, bool hideWhenDone = false)
    {
        _state.SetError(message);
        UpdateVisualState();

        await Task.Delay(_config.AutoHideDelayMs).ConfigureAwait(true);

        _state.Reset();
        UpdateVisualState();
        if (hideWhenDone)
        {
            Hide();
        }
    }

    private void UpdateVisualState()
    {
        MicEllipse.Fill = _state.State switch
        {
            DictationState.Recording => RecordingBrush,
            DictationState.Processing => ProcessingBrush,
            _ => IdleBrush,
        };

        Brush barBrush = _state.State switch
        {
            DictationState.Recording => RecordingBarsBrush,
            DictationState.Processing => ProcessingBarsBrush,
            _ => IdleBrush,
        };
        Bar1.Fill = Bar2.Fill = Bar3.Fill = Bar4.Fill = barBrush;

        bool isActive = _state.State is DictationState.Recording or DictationState.Processing;
        SliverBorder.MinWidth = isActive ? SliverMinWidthActive : SliverMinWidthIdle;
        SliverBorder.Padding = isActive ? SliverPaddingActive : SliverPaddingIdle;
        SliverLift.Y = isActive ? SliverLiftActive : 0;

        bool isRecording = _state.State == DictationState.Recording;
        LevelEllipse.Visibility = isRecording ? Visibility.Visible : Visibility.Hidden;
        if (!isRecording)
        {
            // Collapsed on the way out so the next recording opens from silence rather than
            // inheriting however loud the last word of the previous one happened to be.
            ResetLevel();
        }

        if (_state.State == DictationState.Error && _state.StatusMessage is not null)
        {
            StatusText.Text = _state.StatusMessage;
            StatusText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Visibility = Visibility.Hidden;
        }
    }
}
