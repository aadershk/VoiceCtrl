using System.Windows.Forms;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Input;

/// <summary>
/// The Paste Again key, registered with Windows' <c>RegisterHotKey</c> on a message-only window
/// (spec §12): a true global shortcut, and Windows says when another app already owns it, which
/// the Settings row shows. UI thread only: a hotkey belongs to the thread of its window.
/// </summary>
internal sealed class PasteAgainHotkey : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;

    /// <summary>How long to wait for the chord's keys to come up before pasting anyway.</summary>
    private const int ReleaseWaitMs = 1500;

    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly int[] ModifierVks =
    [
        NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL, NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT,
        NativeMethods.VK_LMENU, NativeMethods.VK_RMENU, NativeMethods.VK_LWIN, NativeMethods.VK_RWIN,
    ];

    private readonly System.Windows.Forms.Timer _releaseWait = new() { Interval = 15 };
    private PasteAgainKey? _registered;
    private long _pressedAt;

    public PasteAgainHotkey()
    {
        CreateHandle(new CreateParams { Parent = HwndMessage });
        _releaseWait.Tick += OnReleaseWaitTick;
    }

    /// <summary>The chord was pressed and its keys are up again: paste now.</summary>
    public event Action? Pressed;

    /// <summary>The saved key couldn't be registered: another app owns it, so Paste Again can't work.</summary>
    public bool IsTaken { get; private set; }

    /// <summary>Makes <paramref name="key"/>, the saved one, the registered key; marks it taken if
    /// Windows refuses. Called at start and whenever the dictation settings change.</summary>
    public void Apply(PasteAgainKey key)
    {
        if (_registered is not null && _registered.SameChord(key))
        {
            IsTaken = false;
            return;
        }

        Unregister();
        IsTaken = !Register(key);
        if (IsTaken)
        {
            SimpleFileLogger.LogInfo($"Paste Again: {string.Join("+", key.Chips)} is taken by another app");
        }
    }

    /// <summary>
    /// For the "Press a key" row: registers <paramref name="key"/> in place of the current one, or
    /// returns false and keeps the current one when Windows refuses it. Saving the key afterwards
    /// makes <see cref="Apply"/> a no-op.
    /// </summary>
    public bool TryRegister(PasteAgainKey key)
    {
        if (_registered is not null && _registered.SameChord(key))
        {
            return true;
        }

        PasteAgainKey? previous = _registered;
        Unregister();
        if (Register(key))
        {
            return true;
        }

        if (previous is not null)
        {
            Register(previous);
        }

        return false;
    }

    private bool Register(PasteAgainKey key)
    {
        if (!Win32.RegisterHotKey(Handle, HotkeyId, key.ModifierFlags | Win32.MOD_NOREPEAT, (uint)key.KeyVk))
        {
            return false;
        }

        _registered = key;
        return true;
    }

    private void Unregister()
    {
        if (_registered is not null)
        {
            Win32.UnregisterHotKey(Handle, HotkeyId);
            _registered = null;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY && m.WParam == HotkeyId)
        {
            // Pasting while Shift (say) is still held would send Ctrl+Shift+V, not Ctrl+V: another
            // paste entirely, and with the default key, this hotkey again.
            _pressedAt = Environment.TickCount64;
            _releaseWait.Start();
            return;
        }

        base.WndProc(ref m);
    }

    private void OnReleaseWaitTick(object? sender, EventArgs e)
    {
        bool held = ModifierVks.Any(vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0);
        if (held && Environment.TickCount64 - _pressedAt < ReleaseWaitMs)
        {
            return;
        }

        _releaseWait.Stop();
        Pressed?.Invoke();
    }

    public void Dispose()
    {
        _releaseWait.Dispose();
        Unregister();
        DestroyHandle();
    }
}
