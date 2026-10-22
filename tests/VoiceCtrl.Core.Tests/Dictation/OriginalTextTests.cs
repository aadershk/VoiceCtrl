using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Smart cleanup's Original travels from the transcription to the kept Dictation (ticket 37).</summary>
public class OriginalTextTests
{
    private static async Task<Rig> DictateAsync(string transcription, string? original, bool pressEnter = false)
    {
        var rig = new Rig { PressEnter = pressEnter, Original = original };
        await rig.Controller.ToggleBar();
        rig.Transcriber.Returns(transcription);
        await rig.Controller.Tap();
        await rig.Controller.Tap();
        return rig;
    }

    [Fact]
    public async Task AnEditedDictation_IsKeptWithItsOriginal()
    {
        Rig rig = await DictateAsync("Send it to Sarah.", "Send it to John, actually, make that Sarah.");

        Assert.Equal("Send it to Sarah.", rig.History.Entries[0].Text);
        Assert.Equal("Send it to John, actually, make that Sarah.", rig.History.Entries[0].Original);
    }

    [Fact]
    public async Task ADictationLeftAlone_HasNoOriginal()
    {
        Rig rig = await DictateAsync("Send it to Sarah.", original: null);

        Assert.Null(rig.History.Entries[0].Original);
    }

    [Fact]
    public async Task APressEnterCommand_IsStrippedFromTheOriginalToo()
    {
        Rig rig = await DictateAsync("See you at noon, press enter.", "See you at noon, uh, press enter.", pressEnter: true);

        Assert.Equal("See you at noon", rig.History.Entries[0].Text);
        Assert.DoesNotContain("press enter", rig.History.Entries[0].Original, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheOriginalIsTakenOnce_SoTheNextDictationDoesNotInheritIt()
    {
        Rig rig = await DictateAsync("Send it to Sarah.", "Send it to John, make that Sarah.");
        rig.Transcriber.Returns("Another one.");
        await rig.Controller.Tap();
        await rig.Controller.Tap();

        Assert.Null(rig.History.Entries[0].Original);
    }
}
