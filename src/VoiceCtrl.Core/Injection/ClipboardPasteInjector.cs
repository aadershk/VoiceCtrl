using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Injection;

/// <summary>
/// Deliberately all-await, never ConfigureAwait(false): the WPF SynchronizationContext is what
/// brings execution back to the STA UI thread after each await, which System.Windows.Clipboard
/// and SendInput both require. Must be called from the UI thread.
/// </summary>
public sealed class ClipboardPasteInjector : ITextInjector
{
    private const int MaxClipboardAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan PostPasteSettleDelay = TimeSpan.FromMilliseconds(100);

    public async Task<InjectionResult> InjectAsync(string text)
    {
        try
        {
            if (ElevationChecker.IsForegroundWindowElevated())
            {
                await SetClipboardTextWithRetryAsync(text).ConfigureAwait(true);
                return InjectionResult.ClipboardOnlyElevatedTarget;
            }

            if (await FocusInspector.InspectAsync().ConfigureAwait(true) == FocusKind.NotTextInput)
            {
                await SetClipboardTextWithRetryAsync(text).ConfigureAwait(true);
                return InjectionResult.ClipboardOnlyNoTextBox;
            }

            IDataObject? saved = TryGetClipboardSnapshot();
            await SetClipboardTextWithRetryAsync(text).ConfigureAwait(true);
            SendInputHelper.SendCtrlV();

            // Give the target app a moment to actually read the clipboard before we overwrite it again.
            await Task.Delay(PostPasteSettleDelay).ConfigureAwait(true);
            await RestoreClipboardWithRetryAsync(saved).ConfigureAwait(true);

            return InjectionResult.Injected;
        }
        catch (COMException ex)
        {
            // Every retry against the clipboard has already been exhausted by this point. The user
            // has spoken words they cannot get back by repeating the gesture, so this returns a
            // result the caller can act on rather than throwing into the generic handler.
            SimpleFileLogger.LogError("Injection", ex);
            return InjectionResult.Failed;
        }
    }

    // InjectAsync has already waited PostPasteSettleDelay after Ctrl+V, so the target has read
    // the clipboard and the text is in place before Enter arrives.
    public void PressEnter() => SendInputHelper.SendKey(NativeMethods.VK_RETURN);

    internal static IDataObject? TryGetClipboardSnapshot()
    {
        try
        {
            // Clipboard.GetDataObject() returns a lazy proxy backed by the live OLE clipboard, not
            // a copy, and its GetData() calls reach through to the *current* clipboard owner. Since we
            // immediately overwrite the clipboard with the paste text, that proxy would be reading
            // through to already-stale content by the time we restore, producing CLIPBRD_E_BAD_DATA
            // (measured directly: SetDataObject(saved, copy: true) at restore time did not throw,
            // but a subsequent GetText() on the "restored" clipboard did). Eagerly materializing
            // every format into a plain in-process DataObject now, while the real owner is still
            // alive, makes the later restore self-contained.
            IDataObject live = Clipboard.GetDataObject();
            var snapshot = new DataObject();
            foreach (string format in live.GetFormats())
            {
                try
                {
                    object? data = live.GetData(format);
                    if (data is not null)
                    {
                        snapshot.SetData(format, data, autoConvert: false);
                    }
                }
                catch (COMException)
                {
                    // One format failed to materialize (e.g. a delayed-rendered bitmap variant).
                    // skip just that format rather than losing the whole snapshot.
                }
            }

            return snapshot;
        }
        catch (COMException)
        {
            // Another process has the clipboard open right now, so nothing to restore later, but
            // that's strictly better than blocking the dictation flow on it.
            return null;
        }
    }

    internal static Task SetClipboardTextWithRetryAsync(string text) =>
        RunWithRetryAsync(() => Clipboard.SetDataObject(PrivateText(text), copy: true));

    /// <summary>
    /// The Dictation as clipboard text that Windows is asked to keep out of clipboard history
    /// (Win+V), cloud clipboard sync and clipboard-monitoring tools. What someone says aloud into
    /// an email is not meant to outlive the paste (VED-49 security review). The three formats are
    /// the documented opt-outs, each a DWORD.
    /// </summary>
    internal static DataObject PrivateText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        return data;
    }

    internal static async Task RestoreClipboardWithRetryAsync(IDataObject? saved)
    {
        if (saved is null)
        {
            return;
        }

        try
        {
            await RunWithRetryAsync(() => Clipboard.SetDataObject(saved, copy: true)).ConfigureAwait(true);
        }
        catch (COMException)
        {
            // Paste already succeeded, and a failed restore is low-severity and must never surface
            // as an in-bar error.
        }
    }

    private static async Task RunWithRetryAsync(Action action)
    {
        for (int attempt = 1; attempt <= MaxClipboardAttempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < MaxClipboardAttempts)
            {
                await Task.Delay(RetryDelay).ConfigureAwait(true);
            }
        }
    }
}
