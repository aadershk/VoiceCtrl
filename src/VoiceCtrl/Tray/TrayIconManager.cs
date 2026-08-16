using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VoiceCtrl.Core.Config;
using Hardcodet.Wpf.TaskbarNotification;

namespace VoiceCtrl.Tray;

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
    private readonly TranscriptionModeStore _modeStore;
    private readonly Dictionary<TranscriptionModePreference, MenuItem> _modeMenuItems = [];

    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public bool IsPaused { get; private set; }

    public TrayIconManager(TranscriptionModeStore modeStore)
    {
        _modeStore = modeStore;

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

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var quitItem = new MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => QuitRequested?.Invoke();

        var menu = new ContextMenu();
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(modeMenu);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(quitItem);

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
