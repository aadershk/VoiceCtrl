using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Tray;

/// <summary>
/// The notification-area icon (DESIGN §12). Left-click opens the Hub; right-click opens the tray
/// flyout. The tooltip says whether VoiceCtrl is ready or paused. The icon keeps v3's
/// taskbar-theme logic: a dark container on a light taskbar, a light one on a dark taskbar.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TaskbarThemeWatcher _taskbarTheme = new();
    private readonly SynchronizationContext _ui;
    private Icon? _icon;

    public TrayIcon(SynchronizationContext ui)
    {
        _ui = ui;

        _notifyIcon = new NotifyIcon();
        SetPaused(false);
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                HubRequested?.Invoke();
            }
            else if (e.Button == MouseButtons.Right)
            {
                FlyoutRequested?.Invoke(IconRect());
            }
        };

        ApplyIcon();
        _taskbarTheme.TaskbarThemeChanged += OnTaskbarThemeChanged;
        _notifyIcon.Visible = true;
    }

    public event Action? HubRequested;

    /// <summary>A right-click, with the icon's rectangle in physical screen pixels.</summary>
    public event Action<PixelRect>? FlyoutRequested;

    /// <summary>"VoiceCtrl · Ready" or "VoiceCtrl · Paused" (DESIGN §12).</summary>
    public void SetPaused(bool paused)
    {
        string name = UserDataPaths.SandboxRoot is null ? "VoiceCtrl" : "VoiceCtrl (sandbox)";
        _notifyIcon.Text = $"{name} · {(paused ? "Paused" : "Ready")}";
    }

    /// <summary>
    /// Where the icon is, from the shell (<c>Shell_NotifyIconGetRect</c>). WinForms keeps the
    /// icon's window and id private, so they are read by reflection; if that or the shell call
    /// fails, the pointer (which has just right-clicked the icon) stands in for it.
    /// </summary>
    private PixelRect IconRect()
    {
        try
        {
            const BindingFlags field = BindingFlags.Instance | BindingFlags.NonPublic;
            object? window = typeof(NotifyIcon).GetField("_window", field)?.GetValue(_notifyIcon);
            object? id = typeof(NotifyIcon).GetField("_id", field)?.GetValue(_notifyIcon);
            if (window is NativeWindow { Handle: var handle } && handle != IntPtr.Zero && id is int or uint)
            {
                var identifier = new Win32.NOTIFYICONIDENTIFIER
                {
                    cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONIDENTIFIER>(),
                    hWnd = handle,
                    uID = Convert.ToUInt32(id),
                };
                if (Win32.Shell_NotifyIconGetRect(ref identifier, out Win32.RECT rect) == 0 && rect.Right > rect.Left)
                {
                    return new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                }
            }
        }
        catch (Exception ex) when (ex is FieldAccessException or TargetException or OverflowException)
        {
            SimpleFileLogger.LogError("Tray icon rectangle", ex);
        }

        SimpleFileLogger.LogInfo("Tray icon rectangle unavailable; the flyout opens by the pointer");
        Point pointer = Cursor.Position;
        return new PixelRect(pointer.X, pointer.Y, 1, 1);
    }

    private void OnTaskbarThemeChanged() => _ui.Post(_ => ApplyIcon(), null);

    private void ApplyIcon()
    {
        string fileName = _taskbarTheme.IsTaskbarLightTheme ? "tray-icon.png" : "tray-icon-dark-taskbar.png";
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        try
        {
            Icon next = LoadIcon(path, SystemInformation.SmallIconSize);
            _notifyIcon.Icon = next;
            _icon?.Dispose();
            _icon = next;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            SimpleFileLogger.LogError("Tray icon", ex);
        }
    }

    /// <summary>Scales the PNG to the tray size and turns it into an icon that owns its handle.</summary>
    private static Icon LoadIcon(string pngPath, Size size)
    {
        using var source = new Bitmap(pngPath);
        using var scaled = new Bitmap(source, size);
        IntPtr handle = scaled.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            Win32.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _taskbarTheme.TaskbarThemeChanged -= OnTaskbarThemeChanged;
        _taskbarTheme.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }
}
