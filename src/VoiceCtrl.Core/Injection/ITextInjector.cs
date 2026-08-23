namespace VoiceCtrl.Core.Injection;

public enum InjectionResult
{
    Injected,
    ClipboardOnlyElevatedTarget,

    /// <summary>
    /// Neither the paste nor the clipboard write got through, most often because another process
    /// held the clipboard open for longer than the retries allowed. The text is not lost: the
    /// caller keeps it and the tray's "Copy last transcription" can put it back.
    /// </summary>
    Failed,
}

public interface ITextInjector
{
    /// <summary>
    /// Places <paramref name="text"/> on the clipboard and, unless the foreground window belongs
    /// to a more-elevated process (UIPI would silently swallow synthetic input into it), simulates
    /// Ctrl+V to paste it at the current cursor, then restores the prior clipboard contents.
    /// When the target is elevated, returns <see cref="InjectionResult.ClipboardOnlyElevatedTarget"/>
    /// without simulating paste or restoring the clipboard, so the caller can prompt for a manual
    /// Ctrl+V (real hardware input isn't blocked by UIPI, only synthetic input is) with the text
    /// still in place to paste.
    /// </summary>
    Task<InjectionResult> InjectAsync(string text);
}
