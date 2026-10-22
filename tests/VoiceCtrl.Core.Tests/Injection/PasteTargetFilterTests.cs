using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Injection;

public class PasteTargetFilterTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("Progman")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("")]
    [InlineData(null)]
    public void WindowsShellSurfaces_AreNeverAPasteTarget(string? className)
    {
        Assert.False(PasteTargetFilter.IsPasteTarget(className));
    }

    [Theory]
    [InlineData("rctrl_renwnd32")] // Outlook
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("Notepad")]
    public void AppWindows_Are(string className)
    {
        Assert.True(PasteTargetFilter.IsPasteTarget(className));
    }
}
