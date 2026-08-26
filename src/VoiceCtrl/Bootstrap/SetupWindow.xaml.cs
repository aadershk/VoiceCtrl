using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Bootstrap;

/// <summary>
/// One-time first-run onboarding: pick a transcription mode, then either verify a Gemini API key
/// or download the offline speech model with visible progress. Replaces the old console prompt so
/// non-technical users get real feedback instead of typing "1" or "2" into a black window.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _downloadCts;
    private bool _keyConfirmedInvalid;

    /// <summary>Well-defined no matter how the window closes: X button, Alt+F4, or a real
    /// completion all leave these at a safe default (Online, no key) unless the user actually
    /// chose Offline or confirmed a key via Continue.</summary>
    public bool Offline { get; private set; }
    public string ApiKey { get; private set; } = string.Empty;

    public SetupWindow()
    {
        InitializeComponent();
    }

    private void OnOnlineCardClicked(object sender, RoutedEventArgs e)
    {
        Offline = false;
        ShowPanel(OnlinePanel);
    }

    private void OnOfflineCardClicked(object sender, RoutedEventArgs e)
    {
        Offline = true;
        ShowPanel(OfflinePanel);
        StartDownload();
    }

    private void OnBackToModeClicked(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        _downloadCts = null;
        Offline = false;
        ShowPanel(ModePanel);
    }

    private void OnOpenApiKeyPageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceCtrl] Failed to open API key page: {ex}");
        }
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        _keyConfirmedInvalid = false;
        OnlineStatusText.Visibility = Visibility.Collapsed;
    }

    private void OnOnlineSkipClicked(object sender, RoutedEventArgs e) =>
        CompleteSetup(offline: false, apiKey: string.Empty);

    private async void OnOnlineContinueClicked(object sender, RoutedEventArgs e)
    {
        string key = ApiKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            CompleteSetup(offline: false, apiKey: string.Empty);
            return;
        }

        // A confirmed-invalid key means the user already saw the warning once and clicked
        // Continue again anyway: proceed without re-checking, same key, same verdict.
        if (!_keyConfirmedInvalid)
        {
            OnlineContinueButton.IsEnabled = false;
            OnlineStatusText.Foreground = Brushes.DimGray;
            OnlineStatusText.Text = "Checking key...";
            OnlineStatusText.Visibility = Visibility.Visible;

            GeminiApiKeyValidator.Result result = await GeminiApiKeyValidator.ValidateAsync(key, _httpClient);

            OnlineContinueButton.IsEnabled = true;

            if (result == GeminiApiKeyValidator.Result.InvalidKey)
            {
                OnlineStatusText.Foreground = Brushes.Firebrick;
                OnlineStatusText.Text = "That key doesn't look right. Double-check it at Google AI Studio, or click Continue again to use it anyway.";
                _keyConfirmedInvalid = true;
                return;
            }

            // Valid, or the check itself failed (NetworkError/Timeout/Unknown): none of those
            // should block setup. AdaptiveTranscriptionClient already tolerates a bad or unset
            // key gracefully at runtime, falling back to offline in Auto mode.
            OnlineStatusText.Visibility = Visibility.Collapsed;
        }

        CompleteSetup(offline: false, apiKey: key);
    }

    private void OnOfflineSkipClicked(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        CompleteSetup(offline: true, apiKey: string.Empty);
    }

    private void OnOfflineRetryClicked(object sender, RoutedEventArgs e) => StartDownload();

    private void OnOfflineContinueClicked(object sender, RoutedEventArgs e) =>
        CompleteSetup(offline: true, apiKey: string.Empty);

    private void OnGetStartedClicked(object sender, RoutedEventArgs e) => Close();

    private void StartDownload()
    {
        string modelDirectory = LocalModelDownloader.DefaultModelDirectory;

        if (LocalModelDownloader.IsFullyDownloaded(modelDirectory))
        {
            ShowDownloadReady();
            return;
        }

        _downloadCts = new CancellationTokenSource();
        _ = RunDownloadAsync(modelDirectory, _downloadCts.Token);
    }

    private async Task RunDownloadAsync(string modelDirectory, CancellationToken cancellationToken)
    {
        OfflineErrorText.Visibility = Visibility.Collapsed;
        OfflineRetryButton.Visibility = Visibility.Collapsed;
        OfflineContinueButton.IsEnabled = false;
        DownloadProgressBar.Value = 0;
        OfflineStatusText.Text = "Downloading speech model...";

        var progress = new Progress<long>(bytesSoFar =>
        {
            int percent = LocalModelDownloader.ComputeDisplayPercent(bytesSoFar);
            DownloadProgressBar.Value = percent;
            OfflineStatusText.Text = $"Downloading speech model... {percent}%";
        });

        try
        {
            await LocalModelDownloader.DownloadAllAsync(modelDirectory, _httpClient, progress, cancellationToken)
                .ConfigureAwait(true);

            ShowDownloadReady();
        }
        catch (OperationCanceledException)
        {
            // User clicked Back/Skip, or closed the window. Nothing further to do: the lazy
            // download path in LocalTranscriptionClient picks this back up on first dictation.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceCtrl] Offline model download failed: {ex}");
            OfflineStatusText.Text = "Download didn't finish.";
            OfflineErrorText.Text = "Couldn't download the speech model. Check your internet connection and retry, or skip for now — it downloads automatically the first time you dictate.";
            OfflineErrorText.Visibility = Visibility.Visible;
            OfflineRetryButton.Visibility = Visibility.Visible;
        }
    }

    private void ShowDownloadReady()
    {
        DownloadProgressBar.Value = 100;
        OfflineStatusText.Text = "✓ Ready to use";
        OfflineContinueButton.IsEnabled = true;
    }

    private void CompleteSetup(bool offline, string apiKey)
    {
        Offline = offline;
        ApiKey = apiKey;
        ShowPanel(FinishPanel);
    }

    private void ShowPanel(UIElement panel)
    {
        ModePanel.Visibility = ReferenceEquals(panel, ModePanel) ? Visibility.Visible : Visibility.Collapsed;
        OnlinePanel.Visibility = ReferenceEquals(panel, OnlinePanel) ? Visibility.Visible : Visibility.Collapsed;
        OfflinePanel.Visibility = ReferenceEquals(panel, OfflinePanel) ? Visibility.Visible : Visibility.Collapsed;
        FinishPanel.Visibility = ReferenceEquals(panel, FinishPanel) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _downloadCts?.Cancel();
        _httpClient.Dispose();
    }
}
