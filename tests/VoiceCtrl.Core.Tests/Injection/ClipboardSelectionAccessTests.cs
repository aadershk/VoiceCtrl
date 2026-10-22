using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Injection;

/// <summary>The rules for reading a selection through the clipboard and the keyboard, against a
/// scripted Windows: what the focused app does with Ctrl+C and the arrow keys.</summary>
public class ClipboardSelectionAccessTests
{
    private sealed class FakeOs : ISelectionOs
    {
        public bool Elevated { get; set; }

        public bool ModifiersRelease { get; set; } = true;

        /// <summary>What the app copies on Ctrl+C; null copies nothing, as an app with no selection does.</summary>
        public Func<string?> OnCopy { get; set; } = () => null;

        public string? Clipboard { get; set; } = "what the user had";

        public List<string> Log { get; } = [];

        public bool IsTargetElevated() => Elevated;

        public Task<bool> WaitForModifiersReleasedAsync(CancellationToken cancellationToken) => Task.FromResult(ModifiersRelease);

        public object? SaveClipboard() => Clipboard;

        public Task RestoreClipboardAsync(object? saved)
        {
            Clipboard = (string?)saved;
            Log.Add("restore");
            return Task.CompletedTask;
        }

        public Task SetClipboardTextAsync(string text)
        {
            Clipboard = text;
            Log.Add("sentinel");
            return Task.CompletedTask;
        }

        public string? ReadClipboardText() => Clipboard;

        public void SendCopy()
        {
            Log.Add("copy");
            if (OnCopy() is { } copied)
            {
                Clipboard = copied;
            }
        }

        public void SendSelectLeft(int count) => Log.Add($"left:{count}");

        public void SendCollapseToRight() => Log.Add("collapse");
    }

    private static ClipboardSelectionAccess Access(FakeOs os) =>
        new(os, copyTimeout: TimeSpan.FromMilliseconds(60), pollInterval: TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task Selection_IsReturned_AndTheClipboardPutBack()
    {
        var os = new FakeOs { OnCopy = () => "the selected words" };

        string? selected = await Access(os).CopySelectionAsync(CancellationToken.None);

        Assert.Equal("the selected words", selected);
        Assert.Equal("what the user had", os.Clipboard);
        Assert.Equal(["sentinel", "copy", "restore"], os.Log);
    }

    [Fact]
    public async Task NothingSelected_IsNull_AndTheClipboardPutBack()
    {
        var os = new FakeOs();

        string? selected = await Access(os).CopySelectionAsync(CancellationToken.None);

        Assert.Null(selected);
        Assert.Equal("what the user had", os.Clipboard);
    }

    [Fact]
    public async Task AnEmptyCopy_IsNothingSelected()
    {
        var os = new FakeOs { OnCopy = () => "" };

        Assert.Null(await Access(os).CopySelectionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ElevatedTarget_IsNotTouched()
    {
        var os = new FakeOs { Elevated = true, OnCopy = () => "text" };

        Assert.Null(await Access(os).CopySelectionAsync(CancellationToken.None));
        Assert.Empty(os.Log);
    }

    [Fact]
    public async Task KeysStillHeld_NothingIsSent()
    {
        var os = new FakeOs { ModifiersRelease = false, OnCopy = () => "text" };

        Assert.Null(await Access(os).CopySelectionAsync(CancellationToken.None));
        Assert.DoesNotContain("copy", os.Log);
    }

    [Fact]
    public async Task SelectPreceding_WalksBackOneStepPerCharacter_AndConfirmsWhatItSelected()
    {
        var os = new FakeOs { OnCopy = () => "Hello there." };

        bool selected = await Access(os).SelectPrecedingAsync("Hello there.", CancellationToken.None);

        Assert.True(selected);
        Assert.Equal(["left:12", "sentinel", "copy", "restore"], os.Log);
    }

    [Fact]
    public async Task SelectPreceding_CountsAnEmojiAndALineBreakAsOneStepEach()
    {
        var os = new FakeOs { OnCopy = () => "a😀\r\nb" };

        await Access(os).SelectPrecedingAsync("a😀\r\nb", CancellationToken.None);

        Assert.Contains("left:4", os.Log);
    }

    [Fact]
    public async Task SelectPreceding_IgnoresLineEndingAndSpacingDifferences()
    {
        var os = new FakeOs { OnCopy = () => "line one\nline  two " };

        Assert.True(await Access(os).SelectPrecedingAsync("line one\r\nline two", CancellationToken.None));
    }

    [Fact]
    public async Task SelectPreceding_WhenTheTextIsNotThere_PutsTheCaretBack()
    {
        var os = new FakeOs { OnCopy = () => "something else entirely" };

        bool selected = await Access(os).SelectPrecedingAsync("Hello there.", CancellationToken.None);

        Assert.False(selected);
        Assert.Equal("collapse", os.Log[^1]);
    }

    [Fact]
    public async Task SelectPreceding_WhenNothingCopies_PutsTheCaretBack()
    {
        var os = new FakeOs();

        Assert.False(await Access(os).SelectPrecedingAsync("Hello there.", CancellationToken.None));
        Assert.Equal("collapse", os.Log[^1]);
    }

    [Theory]
    [InlineData("")]
    public async Task SelectPreceding_NothingToSelect_SendsNothing(string text)
    {
        var os = new FakeOs();

        Assert.False(await Access(os).SelectPrecedingAsync(text, CancellationToken.None));
        Assert.Empty(os.Log);
    }

    [Fact]
    public async Task SelectPreceding_AHugeText_IsRefusedWithoutSendingKeys()
    {
        var os = new FakeOs { OnCopy = () => "x" };

        Assert.False(await Access(os).SelectPrecedingAsync(new string('x', 2001), CancellationToken.None));
        Assert.Empty(os.Log);
    }
}
