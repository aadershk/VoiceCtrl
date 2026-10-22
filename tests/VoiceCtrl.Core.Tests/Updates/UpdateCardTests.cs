using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Core.Tests.Updates;

public sealed class UpdateCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 41, 0, TimeSpan.Zero);

    [Fact]
    public void AFreshInstall_OrAV3Upgrade_ShowsNoCard_AndRemembersThisVersion()
    {
        // Neither has a lastRunVersion; upgraders from v3 get no notice (owner, VED-31).
        AppSection app = UpdateCard.AtStart(new AppSection(), "4.0.0", Now);

        Assert.Equal("4.0.0", app.LastRunVersion);
        Assert.Null(UpdateCard.Visible(app, Now));
    }

    [Fact]
    public void TheFirstStartOfANewerVersion_ShowsTheCard()
    {
        AppSection app = UpdateCard.AtStart(new AppSection { LastRunVersion = "4.0.0" }, "4.1.0", Now);

        Assert.Equal("4.1.0", app.LastRunVersion);
        Assert.Equal("4.1.0", UpdateCard.Visible(app, Now));
    }

    [Fact]
    public void LaterStartsOfTheSameVersion_KeepTheCard()
    {
        AppSection updated = UpdateCard.AtStart(new AppSection { LastRunVersion = "4.0.0" }, "4.1.0", Now);

        AppSection again = UpdateCard.AtStart(updated, "4.1.0", Now.AddDays(2));

        Assert.Equal("4.1.0", UpdateCard.Visible(again, Now.AddDays(2)));
    }

    [Fact]
    public void TheCard_LeavesSevenDaysAfterTheUpdate()
    {
        AppSection app = UpdateCard.AtStart(new AppSection { LastRunVersion = "4.0.0" }, "4.1.0", Now);

        Assert.Equal("4.1.0", UpdateCard.Visible(app, Now.AddDays(7).AddMinutes(-1)));
        Assert.Null(UpdateCard.Visible(app, Now.AddDays(7)));
    }

    [Fact]
    public void Dismissing_RemovesTheCardForGood()
    {
        AppSection app = UpdateCard.AtStart(new AppSection { LastRunVersion = "4.0.0" }, "4.1.0", Now);

        AppSection dismissed = UpdateCard.Dismiss(app);
        AppSection restarted = UpdateCard.AtStart(dismissed, "4.1.0", Now.AddHours(1));

        Assert.Null(UpdateCard.Visible(restarted, Now.AddHours(1)));
    }

    [Fact]
    public void ANewerUpdate_ReplacesTheCard()
    {
        AppSection first = UpdateCard.AtStart(new AppSection { LastRunVersion = "4.0.0" }, "4.1.0", Now);

        AppSection second = UpdateCard.AtStart(first, "4.1.1", Now.AddDays(3));

        Assert.Equal("4.1.1", UpdateCard.Visible(second, Now.AddDays(9)));
    }

    [Fact]
    public void GoingBackToAnOlderVersion_ShowsNoCard()
    {
        AppSection updated = UpdateCard.AtStart(new AppSection { LastRunVersion = "4.0.0" }, "4.1.0", Now);

        AppSection wentBack = UpdateCard.AtStart(updated, "4.0.0", Now.AddHours(1));

        Assert.Equal("4.0.0", wentBack.LastRunVersion);
        Assert.Null(UpdateCard.Visible(wentBack, Now.AddHours(1)));
    }

    [Fact]
    public void AStartOfAVersionNewerThanTheOneSkipped_ForgetsTheSkip()
    {
        var app = new AppSection { LastRunVersion = "4.0.0", SkippedUpdateVersion = "4.1.0" };

        Assert.Equal("4.1.0", UpdateCard.AtStart(app, "4.0.0", Now).SkippedUpdateVersion);
        Assert.Null(UpdateCard.AtStart(app, "4.1.1", Now).SkippedUpdateVersion);
    }

    [Theory]
    [InlineData("4.0.1", "4.0.0", 1)]
    [InlineData("4.0.0", "4.0.1", -1)]
    [InlineData("4.10.0", "4.9.0", 1)]
    [InlineData("4.0.0", "4.0.0", 0)]
    // SemVer precedence: a pre-release is older than its release, and rc.N compares by number.
    // The trial's 4.0.0-rc.N installs must see 4.0.0 as an update (VED-49).
    [InlineData("4.0.0", "4.0.0-rc.1", 1)]
    [InlineData("4.0.0-rc.1", "4.0.0", -1)]
    [InlineData("4.0.0-rc.2", "4.0.0-rc.1", 1)]
    [InlineData("4.0.0-rc.10", "4.0.0-rc.9", 1)]
    [InlineData("4.0.0-rc.1", "4.0.0-rc.1", 0)]
    [InlineData("4.0.1-rc.1", "4.0.0", 1)]
    [InlineData("4.0.0-rc.1+abc123", "4.0.0-rc.1", 0)]
    public void Versions_CompareByNumber(string a, string b, int sign)
    {
        Assert.Equal(sign, Math.Sign(AppVersion.Compare(a, b)));
    }
}
