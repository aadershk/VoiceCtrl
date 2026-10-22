using VoiceCtrl.Core.Setup;

namespace VoiceCtrl.Core.Tests.Setup;

public sealed class ModelDownloadTests
{
    private const long MiB = 1024 * 1024;
    private const long Total = 700 * MiB;

    private readonly FakeDownload _fake = new();
    private readonly List<ModelDownloadStatus> _changes = [];
    private TimeSpan _now = TimeSpan.Zero;

    private ModelDownload Create(bool installed = false, long bytesOnDisk = 0)
    {
        var download = new ModelDownload(_fake.Run, () => installed, () => bytesOnDisk, Total, () => _now);
        download.Changed += _changes.Add;
        return download;
    }

    [Fact]
    public async Task AnInstalledModel_StartsDone()
    {
        ModelDownload download = Create(installed: true);

        Assert.Equal(ModelDownloadState.Done, download.Status.State);
        Assert.Equal(Total, download.Status.Bytes);
        await download.Start();
        Assert.Equal(0, _fake.Runs);
    }

    [Fact]
    public void BeforeStarting_ShowsWhatAnEarlierRunLeftOnDisk()
    {
        ModelDownload download = Create(bytesOnDisk: 312 * MiB);

        Assert.Equal(new ModelDownloadStatus(ModelDownloadState.Idle, 312 * MiB, Total, 0), download.Status);
    }

    [Fact]
    public void Progress_IsReportedAboutOncePerMiB_WithTheRate()
    {
        ModelDownload download = Create();
        _ = download.Start();
        Assert.Equal(ModelDownloadState.Downloading, download.Status.State);

        _now = TimeSpan.FromSeconds(2);
        _fake.Report(MiB / 2);
        _fake.Report(4 * MiB);

        ModelDownloadStatus last = _changes[^1];
        Assert.Equal(2, _changes.Count); // Start, then the 4 MiB report; the half MiB is swallowed.
        Assert.Equal(4 * MiB, last.Bytes);
        Assert.Equal(2 * MiB, last.BytesPerSecond);
    }

    [Fact]
    public void TheRate_CountsOnlyThisRun_NotWhatWasAlreadyOnDisk()
    {
        ModelDownload download = Create(bytesOnDisk: 300 * MiB);
        _ = download.Start();

        _now = TimeSpan.FromSeconds(4);
        _fake.Report(308 * MiB);

        Assert.Equal(2 * MiB, _changes[^1].BytesPerSecond);
    }

    [Fact]
    public async Task Finishing_IsDone()
    {
        ModelDownload download = Create();
        Task run = download.Start();

        _fake.Complete();
        await run;

        Assert.Equal(new ModelDownloadStatus(ModelDownloadState.Done, Total, Total, 0), download.Status);
        Assert.Equal(download.Status, _changes[^1]);
    }

    [Fact]
    public async Task AFailure_HoldsWhereItStopped_AndStartResumes()
    {
        ModelDownload download = Create();
        Task run = download.Start();
        _fake.Report(322 * MiB);

        _fake.Fail(new System.Net.Http.HttpRequestException("gone"));
        await run;

        Assert.Equal(ModelDownloadState.Failed, download.Status.State);
        Assert.Equal(322 * MiB, download.Status.Bytes);

        _ = download.Start();
        Assert.Equal(2, _fake.Runs);
        Assert.Equal(ModelDownloadState.Downloading, download.Status.State);
        Assert.Equal(322 * MiB, download.Status.Bytes);
    }

    [Fact]
    public async Task Stop_CancelsQuietly_AndIgnoresLateReports()
    {
        ModelDownload download = Create();
        Task run = download.Start();
        _fake.Report(10 * MiB);

        download.Stop();
        Assert.True(_fake.Token.IsCancellationRequested);
        Assert.Equal(ModelDownloadState.Idle, download.Status.State);
        Assert.Equal(10 * MiB, download.Status.Bytes);

        _fake.Report(20 * MiB);
        _fake.Fail(new OperationCanceledException());
        await run;

        Assert.Equal(ModelDownloadState.Idle, download.Status.State);
        Assert.Equal(10 * MiB, download.Status.Bytes);
    }

    [Fact]
    public void Start_WhileDownloading_DoesNothing()
    {
        ModelDownload download = Create();
        _ = download.Start();
        _ = download.Start();

        Assert.Equal(1, _fake.Runs);
    }

    /// <summary>A download the test drives by hand: it reports, completes or fails on cue.</summary>
    private sealed class FakeDownload
    {
        private IProgress<long>? _progress;
        private TaskCompletionSource? _run;

        public int Runs { get; private set; }

        public CancellationToken Token { get; private set; }

        public Task Run(IProgress<long> progress, CancellationToken cancellationToken)
        {
            Runs++;
            _progress = progress;
            Token = cancellationToken;
            _run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _run.Task;
        }

        public void Report(long bytes) => _progress!.Report(bytes);

        public void Complete() => _run!.SetResult();

        public void Fail(Exception ex) => _run!.SetException(ex);
    }
}
