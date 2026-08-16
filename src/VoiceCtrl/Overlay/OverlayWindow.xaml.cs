using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Overlay;

public partial class OverlayWindow : Window
{
    private static readonly SolidColorBrush IdleBrush = new(Color.FromRgb(0x50, 0x50, 0x50));
    private static readonly SolidColorBrush RecordingBrush = new(Color.FromRgb(0xE0, 0x40, 0x2A));
    private static readonly SolidColorBrush ProcessingBrush = new(Color.FromRgb(0xC0, 0x8A, 0x2E));

    private readonly OverlayViewModel _viewModel = new();
    private readonly WasapiAudioRecorder _recorder = new();
    private readonly AppConfig _config;
    private readonly ITranscriptionClient _transcriptionClient;
    private readonly TranscriptionModeStore _modeStore;
    private readonly ITextInjector _textInjector;
    private bool _isProcessingStop;

    public OverlayWindow(AppConfig config, ITranscriptionClient transcriptionClient, TranscriptionModeStore modeStore, ITextInjector textInjector)
    {
        InitializeComponent();
        _config = config;
        _transcriptionClient = transcriptionClient;
        _modeStore = modeStore;
        _textInjector = textInjector;
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

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowAtBottomCenter();
        }
    }

    // AdaptiveTranscriptionClient.FellBackToOffline can fire from a background thread (raised after
    // an awaited network call fails), so this dispatches explicitly rather than touching StatusText
    // directly. Skips _viewModel/UpdateVisualState on purpose, since routing through SetError would flip
    // the mic ellipse back to idle-grey while the local model is still actually transcribing.
    public void ShowFellBackToOfflineNotice()
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = "Using offline mode";
            StatusText.Visibility = Visibility.Visible;
        });
    }

    private void ShowAtBottomCenter()
    {
        _viewModel.Reset();
        UpdateVisualState();

        // Width/Height are fixed in XAML (not SizeToContent), so the final position is known
        // before Show(), which avoids a show-then-jump flicker from positioning after layout.
        Point pos = MonitorPositioner.GetBottomCenterPosition(Width, Height);
        Left = pos.X;
        Top = pos.Y;

        Show();
    }

    private async void MicArea_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Guards against a click landing while the previous stop's transcribe/inject pipeline is
        // still in flight, and the view model can already be back at Idle/Error by then (e.g. a
        // fresh double-tap reset it), so without this a fast re-click could call Start() while
        // the recorder is still mid-stop or the last clipboard restore hasn't happened yet.
        if (_isProcessingStop)
        {
            return;
        }

        if (_viewModel.State == OverlayState.Idle)
        {
            try
            {
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

            _viewModel.ToggleRecording();
            UpdateVisualState();
            return;
        }

        if (_viewModel.State != OverlayState.Recording)
        {
            // Processing or Error, so ignore extra clicks rather than re-entering the pipeline.
            return;
        }

        _viewModel.SetProcessing();
        UpdateVisualState();

        _isProcessingStop = true;
        try
        {
            await RunStopPipelineAsync().ConfigureAwait(true);
        }
        finally
        {
            _isProcessingStop = false;
        }
    }

    private async Task RunStopPipelineAsync()
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
            await ShowTransientMessageAsync("Recording failed").ConfigureAwait(true);
            return;
        }

        long stopElapsedMs = pipelineStopwatch.ElapsedMilliseconds;

        if (clip.IsLikelySilent())
        {
            await ShowTransientMessageAsync("No speech detected").ConfigureAwait(true);
            return;
        }

        if (_modeStore.Current == TranscriptionModePreference.Online && !_config.IsApiKeyConfigured)
        {
            await ShowTransientMessageAsync("Add your Gemini API key in .env").ConfigureAwait(true);
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
            await ShowTransientMessageAsync("Transcription failed").ConfigureAwait(true);
            return;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceCtrl] Network error: {ex}");
            SimpleFileLogger.LogError("Network", ex);
            SimpleFileLogger.LogInfo($"Pipeline failed at transcribe after {pipelineStopwatch.ElapsedMilliseconds - stopElapsedMs}ms");
            await ShowTransientMessageAsync("No internet connection").ConfigureAwait(true);
            return;
        }
        catch (TaskCanceledException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceCtrl] Request timed out: {ex}");
            SimpleFileLogger.LogError("Timeout", ex);
            SimpleFileLogger.LogInfo($"Pipeline failed at transcribe after {pipelineStopwatch.ElapsedMilliseconds - stopElapsedMs}ms");
            await ShowTransientMessageAsync("Request timed out").ConfigureAwait(true);
            return;
        }

        long transcribeElapsedMs = pipelineStopwatch.ElapsedMilliseconds;

        if (text is null)
        {
            await ShowTransientMessageAsync("No speech detected").ConfigureAwait(true);
            return;
        }

        InjectionResult result = await _textInjector.InjectAsync(text).ConfigureAwait(true);
        long injectElapsedMs = pipelineStopwatch.ElapsedMilliseconds;

        SimpleFileLogger.LogInfo(
            $"Pipeline timing: mode={_modeStore.Current} stop={stopElapsedMs}ms transcribe={transcribeElapsedMs - stopElapsedMs}ms " +
            $"inject={injectElapsedMs - transcribeElapsedMs}ms total={injectElapsedMs}ms");

        if (result == InjectionResult.ClipboardOnlyElevatedTarget)
        {
            await ShowTransientMessageAsync("Copied. Press Ctrl+V (elevated window)").ConfigureAwait(true);
        }
        else
        {
            _viewModel.Reset();
            Hide();
        }
    }

    private async Task ShowTransientMessageAsync(string message)
    {
        _viewModel.SetError(message);
        UpdateVisualState();

        await Task.Delay(_config.AutoHideDelayMs).ConfigureAwait(true);

        _viewModel.Reset();
        Hide();
    }

    private void UpdateVisualState()
    {
        MicEllipse.Fill = _viewModel.State switch
        {
            OverlayState.Recording => RecordingBrush,
            OverlayState.Processing => ProcessingBrush,
            _ => IdleBrush,
        };

        if (_viewModel.State == OverlayState.Error && _viewModel.StatusMessage is not null)
        {
            StatusText.Text = _viewModel.StatusMessage;
            StatusText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Visibility = Visibility.Hidden;
        }
    }
}
