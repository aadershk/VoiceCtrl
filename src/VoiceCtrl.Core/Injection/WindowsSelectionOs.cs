using System.Runtime.InteropServices;
using System.Windows;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Rewrite;

namespace VoiceCtrl.Core.Injection;

/// <summary>
/// The real clipboard and keyboard for <see cref="ClipboardSelectionAccess"/>. Like
/// <see cref="ClipboardPasteInjector"/>, every await stays on the UI thread (the clipboard and
/// SendInput both need it), and the clipboard is saved and restored the same way.
/// </summary>
internal sealed class WindowsSelectionOs : ISelectionOs
{
    private static readonly TimeSpan ModifierPoll = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan ModifierWait = TimeSpan.FromMilliseconds(1500);

    private static readonly int[] Modifiers =
    [
        NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL, NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT,
        NativeMethods.VK_LMENU, NativeMethods.VK_RMENU, NativeMethods.VK_LWIN, NativeMethods.VK_RWIN,
    ];

    public bool IsTargetElevated() => ElevationChecker.IsForegroundWindowElevated();

    public async Task<bool> WaitForModifiersReleasedAsync(CancellationToken cancellationToken)
    {
        // Ctrl + Shift still down would turn the synthetic Ctrl+C into Ctrl+Shift+C (the browser's
        // inspector, say). Letting go of the Rewrite key is moments away, so this is a short wait.
        for (TimeSpan waited = TimeSpan.Zero; waited < ModifierWait; waited += ModifierPoll)
        {
            if (Modifiers.All(vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) == 0))
            {
                return true;
            }

            await Task.Delay(ModifierPoll, cancellationToken).ConfigureAwait(true);
        }

        return false;
    }

    public object? SaveClipboard() => ClipboardPasteInjector.TryGetClipboardSnapshot();

    public Task RestoreClipboardAsync(object? saved) => ClipboardPasteInjector.RestoreClipboardWithRetryAsync(saved as IDataObject);

    public Task SetClipboardTextAsync(string text) => ClipboardPasteInjector.SetClipboardTextWithRetryAsync(text);

    public string? ReadClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            // Another process has the clipboard open for a moment; the caller polls again.
            return null;
        }
    }

    public void SendCopy() => Send(SendInputHelper.SendCtrlC, "Ctrl+C");

    public void SendSelectLeft(int count) => Send(() => SendInputHelper.SendShiftLeft(count), "Shift+Left");

    public void SendCollapseToRight() => Send(() => SendInputHelper.SendKey(NativeMethods.VK_RIGHT), "Right");

    // SendInput is refused when the focused window runs elevated or the desktop is locked; the
    // selection then reads as empty, which the rewrite reports as nothing to rewrite.
    private static void Send(Action send, string what)
    {
        try
        {
            send();
        }
        catch (InvalidOperationException ex)
        {
            SimpleFileLogger.LogError($"Selection {what}", ex);
        }
    }
}

/// <summary>The selection reader for the real Windows clipboard and keyboard.</summary>
public static class WindowsSelection
{
    public static ISelectionAccess Create() => new ClipboardSelectionAccess(new WindowsSelectionOs());
}
