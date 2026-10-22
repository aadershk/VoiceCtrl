using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Press Enter in a Dictation (CONTEXT; spec §5.3): off by default, and Enter only ever
/// follows a paste that landed.</summary>
public class PressEnterTests
{
    private static async Task<Rig> DictateAsync(string transcription, bool pressEnter = true, InjectionResult paste = InjectionResult.Injected)
    {
        var rig = new Rig { PressEnter = pressEnter };
        rig.Injector.Result = paste;
        await rig.Controller.ToggleBar();
        rig.Transcriber.Returns(transcription);
        await rig.Controller.Tap();
        await rig.Controller.Tap();
        return rig;
    }

    [Fact]
    public async Task On_PastesWithoutTheWords_ThenSendsEnter()
    {
        Rig rig = await DictateAsync("See you at noon, press enter.");

        Assert.Equal(["See you at noon", "⏎"], rig.Injector.Sent);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
    }

    [Fact]
    public async Task On_KeepsTheDictationWithoutTheWords()
    {
        Rig rig = await DictateAsync("See you at noon, press enter.");

        Assert.Equal("See you at noon", rig.Last.Text);
        Assert.Equal("See you at noon", rig.History.Entries[0].Text);
    }

    [Fact]
    public async Task Off_PastesTheWordsAsSaid()
    {
        Rig rig = await DictateAsync("See you at noon, press enter.", pressEnter: false);

        Assert.Equal(["See you at noon, press enter."], rig.Injector.Sent);
    }

    [Fact]
    public async Task On_WithoutAFinalPressEnter_SendsNoEnter()
    {
        Rig rig = await DictateAsync("Press enter to continue.");

        Assert.Equal(["Press enter to continue."], rig.Injector.Sent);
    }

    [Fact]
    public async Task OnlyPressEnter_SendsEnter_AndKeepsNothing()
    {
        Rig rig = await DictateAsync("Press enter.");

        Assert.Equal(["⏎"], rig.Injector.Sent);
        Assert.Empty(rig.History.Entries);
        Assert.Null(rig.Last.Text);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
    }

    [Theory]
    [InlineData(InjectionResult.ClipboardOnlyElevatedTarget)]
    [InlineData(InjectionResult.Failed)]
    public async Task APasteThatDidNotLand_IsNotFollowedByEnter(InjectionResult paste)
    {
        Rig rig = await DictateAsync("Ship it, press enter", paste: paste);

        Assert.Equal(0, rig.Injector.Enters);
        Assert.Equal("Ship it", rig.Last.Text);
    }

    [Fact]
    public async Task PasteAgain_NeverSendsEnter()
    {
        Rig rig = await DictateAsync("Ship it, press enter");
        rig.Timer.AdvanceMs(900);

        await rig.Controller.PasteAgain();

        Assert.Equal(["Ship it", "⏎", "Ship it"], rig.Injector.Sent);
    }
}
