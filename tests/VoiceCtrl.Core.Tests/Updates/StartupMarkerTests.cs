using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Core.Tests.Updates;

public sealed class StartupMarkerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("voicectrl-marker-").FullName;
    private readonly StartupMarker _marker;

    public StartupMarkerTests() => _marker = new StartupMarker(Path.Combine(_folder, "starting.json"));

    [Fact]
    public void AStartThatFinishes_LeavesNoMarker()
    {
        Assert.Null(_marker.Begin("4.0.0", lastRunVersion: "4.0.0"));

        _marker.Complete();

        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void TheFirstStartAfterAnUpdate_ThatNeverFinished_IsReportedNextTime()
    {
        _marker.Begin("4.1.0", lastRunVersion: "4.0.0");

        // The app died before the tray came up. By now lastRunVersion already says 4.1.0.
        FailedUpdate? failed = _marker.Begin("4.1.0", lastRunVersion: "4.1.0");

        Assert.Equal(new FailedUpdate(From: "4.0.0", To: "4.1.0"), failed);
    }

    [Fact]
    public void TryingAgain_AndFailingAgain_IsReportedAgain()
    {
        _marker.Begin("4.1.0", lastRunVersion: "4.0.0");
        _marker.Begin("4.1.0", lastRunVersion: "4.1.0");

        FailedUpdate? again = _marker.Begin("4.1.0", lastRunVersion: "4.1.0");

        Assert.Equal(new FailedUpdate("4.0.0", "4.1.0"), again);
    }

    [Fact]
    public void OnceAStartAfterTheUpdateFinishes_LaterFailuresAreNotBlamedOnIt()
    {
        _marker.Begin("4.1.0", lastRunVersion: "4.0.0");
        _marker.Complete();
        _marker.Begin("4.1.0", lastRunVersion: "4.1.0");

        Assert.Null(_marker.Begin("4.1.0", lastRunVersion: "4.1.0"));
    }

    [Fact]
    public void AnUnfinishedStart_WithNoUpdateBehindIt_IsNotReported()
    {
        _marker.Begin("4.0.0", lastRunVersion: "4.0.0");

        Assert.Null(_marker.Begin("4.0.0", lastRunVersion: "4.0.0"));
    }

    [Fact]
    public void AFreshInstall_IsNotAnUpdate()
    {
        _marker.Begin("4.0.0", lastRunVersion: null);

        Assert.Null(_marker.Begin("4.0.0", lastRunVersion: "4.0.0"));
    }

    [Fact]
    public void AfterGoingBack_TheOlderVersionStartsClean()
    {
        _marker.Begin("4.1.0", lastRunVersion: "4.0.0");

        Assert.Null(_marker.Begin("4.0.0", lastRunVersion: "4.1.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    public void AnUnreadableMarker_CountsAsNone(string text)
    {
        File.WriteAllText(Path.Combine(_folder, "starting.json"), text);

        Assert.Null(_marker.Begin("4.1.0", lastRunVersion: "4.1.0"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
