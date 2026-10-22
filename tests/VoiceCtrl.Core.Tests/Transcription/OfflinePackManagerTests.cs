using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Transcription;

public sealed class OfflinePackManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-packs-{Guid.NewGuid():N}");
    private readonly List<string> _events = [];

    [Fact]
    public void WithNothingInstalled_OfflineUsesEnglish()
    {
        using Harness h = NewHarness();

        Assert.Equal(new OfflinePackState(OfflinePack.None, null, null), h.Manager.State);
        Assert.Equal(OfflineModels.English.Directory, h.Manager.CurrentModel().Directory);
    }

    [Fact]
    public async Task PickingAPack_DownloadsIt_ThenSwitchesOfflineToIt()
    {
        using Harness h = NewHarness();

        await h.Manager.Pick(OfflinePack.European);

        Assert.Equal(OfflinePack.European, h.Manager.State.Installed);
        Assert.Null(h.Manager.State.Target);
        Assert.Equal(OfflinePack.European, h.Settings.Current.Transcription.OfflinePack);
        Assert.Equal(OfflineModels.European.Directory, h.Manager.CurrentModel().Directory);
        Assert.Equal(["download European", "installed"], _events);
    }

    [Fact]
    public async Task WhileDownloading_OfflineKeepsUsingWhatWasInstalled_AndProgressIsReported()
    {
        using Harness h = NewHarness();
        h.Hold = new TaskCompletionSource();
        var changes = new List<OfflinePackState>();
        h.Manager.Changed += () => changes.Add(h.Manager.State);

        Task pick = h.Manager.Pick(OfflinePack.AllLanguages);

        Assert.Equal(OfflinePack.AllLanguages, h.Manager.State.Target);
        Assert.Equal(ModelDownloadState.Downloading, h.Manager.State.Download!.State);
        Assert.Equal(OfflineModels.English.Directory, h.Manager.CurrentModel().Directory);
        Assert.Equal(OfflinePack.None, h.Settings.Current.Transcription.OfflinePack);

        h.Hold.SetResult();
        await pick;
        Assert.Contains(changes, s => s.Download is { State: ModelDownloadState.Downloading, Bytes: > 0 });
    }

    [Fact]
    public async Task ReplacingThePack_LetsGoOfTheModelBeforeDeletingTheOldFiles()
    {
        using Harness h = NewHarness();
        await h.Manager.Pick(OfflinePack.European);
        _events.Clear();

        await h.Manager.Pick(OfflinePack.AllLanguages);

        Assert.Equal(["download AllLanguages", "release", "installed"], _events);
        Assert.False(Directory.Exists(OfflineModels.European.PathUnder(_root)), "the replaced pack's files are gone");
        Assert.True(Directory.Exists(OfflineModels.AllLanguages.PathUnder(_root)));
        Assert.Equal(OfflinePack.AllLanguages, h.Settings.Current.Transcription.OfflinePack);
    }

    [Fact]
    public async Task PickingEnglishOnly_RemovesTheExtraPack()
    {
        using Harness h = NewHarness();
        await h.Manager.Pick(OfflinePack.European);
        _events.Clear();

        await h.Manager.Pick(OfflinePack.None);

        Assert.Equal(["release", "installed"], _events);
        Assert.False(Directory.Exists(OfflineModels.European.PathUnder(_root)));
        Assert.Equal(OfflinePack.None, h.Manager.State.Installed);
        Assert.Equal(OfflineModels.English.Directory, h.Manager.CurrentModel().Directory);
    }

    [Fact]
    public async Task PickingWhatIsAlreadyInstalled_DownloadsNothing()
    {
        using Harness h = NewHarness();
        await h.Manager.Pick(OfflinePack.European);
        _events.Clear();

        await h.Manager.Pick(OfflinePack.European);

        Assert.Empty(_events);
    }

    [Fact]
    public async Task Cancel_DiscardsThePartialPack_AndLeavesTheInstalledOneAlone()
    {
        using Harness h = NewHarness();
        await h.Manager.Pick(OfflinePack.European);
        h.Hold = new TaskCompletionSource();

        Task pick = h.Manager.Pick(OfflinePack.AllLanguages);
        Assert.True(Directory.Exists(OfflineModels.AllLanguages.PathUnder(_root)), "the partial file is on disk");
        h.Manager.Cancel();
        await pick;

        Assert.Null(h.Manager.State.Target);
        Assert.False(Directory.Exists(OfflineModels.AllLanguages.PathUnder(_root)));
        Assert.Equal(OfflinePack.European, h.Manager.State.Installed);
        Assert.True(Directory.Exists(OfflineModels.European.PathUnder(_root)));
    }

    [Fact]
    public async Task AFailedDownload_KeepsItsPartialFiles_SoPickingAgainResumes()
    {
        using Harness h = NewHarness();
        h.FailNext = true;

        await h.Manager.Pick(OfflinePack.European);

        Assert.Equal(OfflinePack.European, h.Manager.State.Target);
        Assert.Equal(ModelDownloadState.Failed, h.Manager.State.Download!.State);
        Assert.Equal(OfflinePack.None, h.Manager.State.Installed);
        Assert.True(File.Exists(Path.Combine(OfflineModels.European.PathUnder(_root), "encoder.int8.onnx.download")));

        await h.Manager.Pick(OfflinePack.European);

        Assert.Equal(OfflinePack.European, h.Manager.State.Installed);
        Assert.Null(h.Manager.State.Target);
    }

    [Fact]
    public async Task PickingAnotherPackWhileDownloading_ReplacesTheDownload()
    {
        using Harness h = NewHarness();
        h.Hold = new TaskCompletionSource();

        Task first = h.Manager.Pick(OfflinePack.European);
        h.Hold = null;
        await h.Manager.Pick(OfflinePack.AllLanguages);
        await first;

        Assert.Equal(OfflinePack.AllLanguages, h.Manager.State.Installed);
        Assert.False(Directory.Exists(OfflineModels.European.PathUnder(_root)), "the abandoned download was discarded");
    }

    [Fact]
    public void ASavedPackWhoseFilesAreGone_CountsAsNotInstalled_SoOfflineFallsBackToEnglish()
    {
        using Harness h = NewHarness(new UserSettings { Transcription = new TranscriptionSection { OfflinePack = OfflinePack.AllLanguages } });

        Assert.Equal(OfflinePack.None, h.Manager.State.Installed);
        Assert.Equal(OfflineModels.English.Directory, h.Manager.CurrentModel().Directory);
    }

    [Fact]
    public void AtStart_ACompleteUnusedPackIsDeleted_ButAPartialOneIsKeptForResuming()
    {
        WriteModel(OfflineModels.European, complete: true);
        WriteModel(OfflineModels.AllLanguages, complete: false);

        using Harness h = NewHarness();

        Assert.False(Directory.Exists(OfflineModels.European.PathUnder(_root)));
        Assert.True(Directory.Exists(OfflineModels.AllLanguages.PathUnder(_root)));
    }

    private Harness NewHarness(UserSettings? initial = null) => new(this, initial ?? new UserSettings());

    private void WriteModel(OfflineModel model, bool complete)
    {
        string directory = model.PathUnder(_root);
        Directory.CreateDirectory(directory);
        foreach (OfflineModelFile file in model.Files)
        {
            File.WriteAllBytes(Path.Combine(directory, file.Local + (complete ? "" : ".download")), [1]);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly OfflinePackManagerTests _test;

        public Harness(OfflinePackManagerTests test, UserSettings initial)
        {
            _test = test;
            Settings = SettingsStore.InMemory(initial);
            Manager = new OfflinePackManager(Settings, test._root, DownloadAsync, ReleaseAsync, () => TimeSpan.Zero);
            Manager.InstalledChanged += () => test._events.Add("installed");
        }

        public SettingsStore Settings { get; }

        public OfflinePackManager Manager { get; }

        /// <summary>While set, a download reports some progress and then waits.</summary>
        public TaskCompletionSource? Hold { get; set; }

        public bool FailNext { get; set; }

        private async Task DownloadAsync(OfflineModel model, IProgress<long> progress, CancellationToken token)
        {
            string pack = model == OfflineModels.European ? "European" : "AllLanguages";
            _test._events.Add($"download {pack}");

            // Read now: the test may set Hold for the next pick while this one is still writing.
            TaskCompletionSource? hold = Hold;
            bool fail = FailNext;
            string directory = model.PathUnder(_test._root);
            Directory.CreateDirectory(directory);

            OfflineModelFile first = model.Files[0];
            await File.WriteAllBytesAsync(Path.Combine(directory, first.Local + ".download"), [1], token);
            progress.Report(model.ApproxBytes / 2);

            if (fail)
            {
                FailNext = false;
                throw new IOException("The connection dropped.");
            }

            if (hold is not null)
            {
                await hold.Task.WaitAsync(token);
            }

            foreach (OfflineModelFile file in model.Files)
            {
                await File.WriteAllBytesAsync(Path.Combine(directory, file.Local), [1], token);
                File.Delete(Path.Combine(directory, file.Local + ".download"));
            }
        }

        private Task ReleaseAsync()
        {
            _test._events.Add("release");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
