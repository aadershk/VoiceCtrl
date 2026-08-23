using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Transcription;
using Hardcodet.Wpf.TaskbarNotification;

namespace VoiceCtrl.Tray;

/// <summary>A user-editable file the tray offers to open, e.g. the custom dictionary.</summary>
public sealed record TrayFileEntry(string Label, string Path);

/// <summary>
/// Owns the tray icon and its context menu. Knows nothing about config paths or app
/// lifecycle, so callers wire behavior through events, the same pattern
/// LowLevelKeyboardHook uses for DoubleTapDetected.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private static readonly TranscriptionModePreference[] ModeMenuOrder =
    [
        TranscriptionModePreference.Auto,
        TranscriptionModePreference.Online,
        TranscriptionModePreference.Offline,
    ];

    private readonly TaskbarIcon _icon;
    private readonly MenuItem _pauseMenuItem;
    private readonly MenuItem _copyLastMenuItem;
    private readonly TranscriptionModeStore _modeStore;
    private readonly LastTranscriptionStore _lastTranscription;
    private readonly Dictionary<TranscriptionModePreference, MenuItem> _modeMenuItems = [];

    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    /// <summary>Raised with the path of a user-editable file the user asked to open. Handled by
    /// the caller so this class keeps knowing nothing about where those files live.</summary>
    public event Action<string>? OpenFileRequested;

    public bool IsPaused { get; private set; }

    public TrayIconManager(
        TranscriptionModeStore modeStore,
        LastTranscriptionStore lastTranscription,
        IReadOnlyList<TrayFileEntry> editableFiles)
    {
        _modeStore = modeStore;
        _lastTranscription = lastTranscription;

        _pauseMenuItem = new MenuItem { Header = "Pause" };
        _pauseMenuItem.Click += (_, _) => TogglePause();

        var modeMenu = new MenuItem { Header = "Mode" };
        foreach (TranscriptionModePreference mode in ModeMenuOrder)
        {
            var modeItem = new MenuItem
            {
                Header = mode.ToString(),
                IsCheckable = true,
                IsChecked = _modeStore.Current == mode,
            };
            modeItem.Click += (_, _) => SelectMode(mode);
            _modeMenuItems[mode] = modeItem;
            modeMenu.Items.Add(modeItem);
        }

        var personalizeMenu = new MenuItem { Header = "Personalize" };
        foreach (TrayFileEntry file in editableFiles)
        {
            var fileItem = new MenuItem { Header = file.Label };
            fileItem.Click += (_, _) => OpenFileRequested?.Invoke(file.Path);
            personalizeMenu.Items.Add(fileItem);
        }

        _copyLastMenuItem = new MenuItem { Header = "Copy last transcription", IsEnabled = false };
        _copyLastMenuItem.Click += (_, _) => CopyLastTranscription();

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var quitItem = new MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => QuitRequested?.Invoke();

        var menu = new ContextMenu();
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(modeMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(personalizeMenu);
        menu.Items.Add(_copyLastMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(quitItem);

        // Evaluated on open rather than pushed on every transcription: the menu is the only thing
        // that reads this, and it is closed almost all of the time.
        menu.Opened += (_, _) => _copyLastMenuItem.IsEnabled = _lastTranscription.Text is not null;

        _icon = new TaskbarIcon
        {
            ToolTipText = "VoiceCtrl: double-tap either Ctrl key to dictate",
            IconSource = LoadIconSource(),
            ContextMenu = menu,
        };
    }

    public void ShowFirstRunBalloon()
    {
        _icon.ShowBalloonTip(
            "VoiceCtrl is running",
            "Double-tap either Ctrl key anywhere to start dictating. Find me in the tray any time.",
            BalloonIcon.Info);
    }

    private void CopyLastTranscription()
    {
        if (_lastTranscription.Text is not { } text)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException ex)
        {
            // Another process is holding the clipboard. The text is still in memory, so the user
            // can simply try the menu entry again once that process lets go.
            SimpleFileLogger.LogError("CopyLastTranscription", ex);
            _icon.ShowBalloonTip(
                "Could not copy",
                "Another program is using the clipboard. Try again in a moment.",
                BalloonIcon.Warning);
        }
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        _pauseMenuItem.Header = IsPaused ? "Resume" : "Pause";
    }

    private void SelectMode(TranscriptionModePreference mode)
    {
        _modeStore.Current = mode;
        _modeStore.Save();

        foreach ((TranscriptionModePreference candidate, MenuItem item) in _modeMenuItems)
        {
            item.IsChecked = candidate == mode;
        }
    }

    private static BitmapImage LoadIconSource()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-icon.png");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose() => _icon.Dispose();
}
