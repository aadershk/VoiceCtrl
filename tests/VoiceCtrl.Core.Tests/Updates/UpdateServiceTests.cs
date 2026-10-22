using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Core.Tests.Updates;

public sealed class UpdateServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 41, 0, TimeSpan.Zero);

    private readonly SettingsStore _settings = SettingsStore.InMemory(new UserSettings());
    private readonly FakeSource _source = new();
    private readonly UpdateService _service;
    private readonly List<UpdateState> _seen = [];

    public UpdateServiceTests()
    {
        _service = new UpdateService(_settings, _source, "4.0.0", () => Now);
        _service.Changed += () => _seen.Add(_service.View().State);
    }

    [Fact]
    public void ByDefault_AutomaticUpdatesAreOn_AndNothingHasBeenChecked()
    {
        UpdateView view = _service.View();

        Assert.Equal(UpdateState.UpToDate, view.State);
        Assert.True(view.Automatic);
        Assert.Equal("4.0.0", view.CurrentVersion);
        Assert.Null(view.LastChecked);
        Assert.False(view.CheckFailed);
    }

    [Fact]
    public async Task Check_FindingNothing_RecordsTheTimeOfTheCheck()
    {
        await _service.CheckAsync();

        UpdateView view = _service.View();
        Assert.Equal(UpdateState.UpToDate, view.State);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), view.LastChecked);
        Assert.Equal(Now, _settings.Current.App.LastUpdateCheck);
        Assert.Equal([UpdateState.Checking, UpdateState.UpToDate], _seen);
    }

    [Fact]
    public async Task Check_WithAutomaticOn_DownloadsWhatItFinds_ThenIsReady()
    {
        _source.Available = "4.1.0";

        await _service.CheckAsync();

        UpdateView view = _service.View();
        Assert.Equal(UpdateState.Ready, view.State);
        Assert.Equal("4.1.0", view.NewVersion);
        Assert.Contains(UpdateState.Downloading, _seen);
        Assert.Equal(["4.1.0"], _source.Downloaded);
    }

    [Fact]
    public async Task WhileDownloading_TheViewCarriesTheProgress()
    {
        _source.Available = "4.1.0";
        _source.HoldDownload = new TaskCompletionSource();
        Task check = _service.CheckAsync();

        _source.Report(0.62);

        UpdateView view = _service.View();
        Assert.Equal(UpdateState.Downloading, view.State);
        Assert.Equal(0.62, view.Progress, precision: 3);
        _source.HoldDownload.SetResult();
        await check;
    }

    [Fact]
    public async Task Check_WithAutomaticOff_OffersTheVersion_AndDownloadsOnlyWhenAsked()
    {
        _service.SetAutomatic(false);
        _source.Available = "4.1.0";

        await _service.CheckAsync();

        Assert.Equal(UpdateState.Available, _service.View().State);
        Assert.Equal("4.1.0", _service.View().NewVersion);
        Assert.Empty(_source.Downloaded);

        await _service.DownloadAsync();

        Assert.Equal(UpdateState.Ready, _service.View().State);
        Assert.Equal(["4.1.0"], _source.Downloaded);
    }

    [Fact]
    public void TurningAutomaticOff_SavesIt_AndShowsOff()
    {
        _service.SetAutomatic(false);

        Assert.False(_settings.Current.App.AutomaticUpdates);
        Assert.Equal(UpdateState.Off, _service.View().State);
        Assert.False(_service.View().Automatic);
    }

    [Fact]
    public async Task TurningAutomaticOff_MidDownload_StopsIt()
    {
        _source.Available = "4.1.0";
        _source.HoldDownload = new TaskCompletionSource();
        Task check = _service.CheckAsync();

        _service.SetAutomatic(false);
        await check;

        Assert.Equal(UpdateState.Off, _service.View().State);
        Assert.True(_source.DownloadCancelled);
    }

    [Fact]
    public async Task ADownloadThatFailsItsCheck_IsDroppedWithoutAMessage()
    {
        _source.Available = "4.1.0";
        _source.FailDownload = true;

        await _service.CheckAsync();

        UpdateView view = _service.View();
        Assert.Equal(UpdateState.UpToDate, view.State);
        Assert.False(view.CheckFailed);
        Assert.Null(view.NewVersion);
    }

    [Fact]
    public async Task ACheckThatCantReachGitHub_KeepsTheLastGoodCheckTime_AndSaysSo()
    {
        await _service.CheckAsync();
        _source.FailCheck = true;

        await _service.CheckAsync();

        UpdateView view = _service.View();
        Assert.Equal(UpdateState.UpToDate, view.State);
        Assert.True(view.CheckFailed);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), view.LastChecked);
    }

    [Fact]
    public async Task ASecondCheck_WhileOneRuns_DoesNothing()
    {
        _source.HoldCheck = new TaskCompletionSource();
        Task first = _service.CheckAsync();

        await _service.CheckAsync();
        _source.HoldCheck.SetResult();
        await first;

        Assert.Equal(1, _source.Checks);
    }

    [Fact]
    public async Task RestartNow_AppliesTheReadyUpdate()
    {
        _source.Available = "4.1.0";
        await _service.CheckAsync();

        _service.RestartNow();

        Assert.Equal("4.1.0", _source.Applied);
    }

    [Fact]
    public void RestartNow_WithNothingReady_DoesNothing()
    {
        _service.RestartNow();

        Assert.Null(_source.Applied);
    }

    [Fact]
    public async Task TurningAutomaticOff_WithAnUpdateReady_DiscardsIt_SoNothingInstallsAtTheNextStart()
    {
        _source.Available = "4.1.0";
        await _service.CheckAsync();

        _service.SetAutomatic(false);

        Assert.Equal(UpdateState.Off, _service.View().State);
        Assert.Equal(["4.1.0"], _source.Discarded);
    }

    [Fact]
    public async Task AScheduledCheck_RunsOnlyWhileAutomaticUpdatesIsOn()
    {
        _service.SetAutomatic(false);
        await _service.ScheduledCheckAsync();
        Assert.Equal(0, _source.Checks);

        _service.SetAutomatic(true);
        await _service.ScheduledCheckAsync();
        Assert.Equal(1, _source.Checks);
    }

    [Fact]
    public async Task AVersionTheUserWentBackFrom_IsNotOfferedAgain()
    {
        _settings.Update(s => s with { App = s.App with { SkippedUpdateVersion = "4.1.0" } });
        _source.Available = "4.1.0";

        await _service.CheckAsync();

        Assert.Equal(UpdateState.UpToDate, _service.View().State);
        Assert.Empty(_source.Downloaded);
    }

    [Fact]
    public async Task AFixNewerThanTheSkippedVersion_IsOffered()
    {
        _settings.Update(s => s with { App = s.App with { SkippedUpdateVersion = "4.1.0" } });
        _source.Available = "4.1.1";

        await _service.CheckAsync();

        Assert.Equal(["4.1.1"], _source.Downloaded);
    }

    [Fact]
    public void TheLastCheck_IsReadBackFromSettings_AfterARestart()
    {
        var settings = SettingsStore.InMemory(new UserSettings { App = new AppSection { LastUpdateCheck = Now.AddDays(-1) } });

        using var service = new UpdateService(settings, _source, "4.0.0", () => Now);

        Assert.Equal(Now.AddDays(-1).ToUnixTimeMilliseconds(), service.View().LastChecked);
    }

    public void Dispose() => _service.Dispose();

    private sealed class FakeSource : IUpdateSource
    {
        private IProgress<double>? _progress;

        public string? Available { get; set; }
        public bool FailCheck { get; set; }
        public bool FailDownload { get; set; }
        public TaskCompletionSource? HoldCheck { get; set; }
        public TaskCompletionSource? HoldDownload { get; set; }
        public int Checks { get; private set; }
        public List<string> Downloaded { get; } = [];
        public bool DownloadCancelled { get; private set; }
        public string? Applied { get; private set; }

        public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            if (HoldCheck is not null)
            {
                await HoldCheck.Task;
            }

            if (FailCheck)
            {
                throw new HttpRequestException("offline");
            }

            return Available is null ? null : new UpdateInfo(Available);
        }

        public async Task DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancellationToken)
        {
            _progress = progress;
            if (HoldDownload is not null)
            {
                using (cancellationToken.Register(() => HoldDownload.TrySetCanceled(cancellationToken)))
                {
                    try
                    {
                        await HoldDownload.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        DownloadCancelled = true;
                        throw;
                    }
                }
            }

            if (FailDownload)
            {
                throw new UpdateVerificationException("signature did not match");
            }

            Downloaded.Add(update.Version);
        }

        public void ApplyAndRestart(UpdateInfo update) => Applied = update.Version;

        public List<string> Discarded { get; } = [];

        public void Discard(UpdateInfo update) => Discarded.Add(update.Version);

        // Progress<T> would post to a context; the fake reports straight through.
        public void Report(double fraction) => _progress?.Report(fraction);
    }
}
