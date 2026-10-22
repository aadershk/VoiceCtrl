using System.Globalization;
using System.Text.RegularExpressions;
using VoiceCtrl.Core.Rewrite;

namespace VoiceCtrl.Core.Injection;

/// <summary>What <see cref="ClipboardSelectionAccess"/> needs from Windows: the clipboard, the
/// keyboard and the real key state. A seam so the rules are tested without touching either.</summary>
internal interface ISelectionOs
{
    /// <summary>True when the focused window runs elevated, so synthetic keys can't reach it.</summary>
    bool IsTargetElevated();

    /// <summary>Waits until Ctrl, Shift, Alt and Win are all physically up. False if one stays down.</summary>
    Task<bool> WaitForModifiersReleasedAsync(CancellationToken cancellationToken);

    /// <summary>Everything on the clipboard now, to be put back by <see cref="RestoreClipboardAsync"/>.</summary>
    object? SaveClipboard();

    Task RestoreClipboardAsync(object? saved);

    /// <summary>Puts <paramref name="text"/> on the clipboard, kept out of clipboard history.</summary>
    Task SetClipboardTextAsync(string text);

    string? ReadClipboardText();

    void SendCopy();

    /// <summary>Shift + Left, <paramref name="count"/> times: selects that many characters before the caret.</summary>
    void SendSelectLeft(int count);

    /// <summary>One Right arrow: ends a selection at its right edge, i.e. where the caret began.</summary>
    void SendCollapseToRight();
}

/// <summary>
/// Reads and re-makes the focused app's selection the one way every app allows: Ctrl+C and the
/// arrow keys, with the clipboard put back afterwards. Copying is detected by planting a sentinel on
/// the clipboard first: if it is still there after Ctrl+C, nothing was selected.
/// </summary>
internal sealed partial class ClipboardSelectionAccess(ISelectionOs os, TimeSpan? copyTimeout = null, TimeSpan? pollInterval = null) : ISelectionAccess
{
    /// <summary>A rewrite of more than this many characters can't be walked back with Shift + Left.</summary>
    private const int MaxSelectSteps = 2000;

    private readonly TimeSpan _copyTimeout = copyTimeout ?? TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(20);

    public async Task<string?> CopySelectionAsync(CancellationToken cancellationToken)
    {
        if (os.IsTargetElevated() || !await os.WaitForModifiersReleasedAsync(cancellationToken).ConfigureAwait(true))
        {
            return null;
        }

        return await CopyAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task<bool> SelectPrecedingAsync(string text, CancellationToken cancellationToken)
    {
        int steps = new StringInfo(text).LengthInTextElements;
        if (steps is 0 or > MaxSelectSteps
            || os.IsTargetElevated()
            || !await os.WaitForModifiersReleasedAsync(cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        os.SendSelectLeft(steps);
        string? selected = await CopyAsync(cancellationToken).ConfigureAwait(true);
        if (selected is not null && Normalize(selected) == Normalize(text))
        {
            return true;
        }

        // The text isn't there (the caret moved, or the app edited it): put the caret back.
        os.SendCollapseToRight();
        return false;
    }

    /// <summary>Ctrl+C, and what it put on the clipboard; null when nothing was copied.</summary>
    private async Task<string?> CopyAsync(CancellationToken cancellationToken)
    {
        object? saved = os.SaveClipboard();
        string sentinel = "voicectrl-selection-" + Guid.NewGuid().ToString("N");
        try
        {
            await os.SetClipboardTextAsync(sentinel).ConfigureAwait(true);
            os.SendCopy();

            for (TimeSpan waited = TimeSpan.Zero; waited < _copyTimeout; waited += _pollInterval)
            {
                string? text = os.ReadClipboardText();
                if (!string.IsNullOrEmpty(text) && text != sentinel)
                {
                    return text;
                }

                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(true);
            }

            return null;
        }
        finally
        {
            await os.RestoreClipboardAsync(saved).ConfigureAwait(true);
        }
    }

    /// <summary>Apps differ in line endings and trailing spaces, never in the words.</summary>
    private static string Normalize(string text) => WhitespacePattern().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
