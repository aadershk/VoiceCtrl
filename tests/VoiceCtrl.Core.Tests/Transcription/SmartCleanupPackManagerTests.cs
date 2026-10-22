using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public sealed class SmartCleanupPackManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-llm-{Guid.NewGuid():N}");
    private readonly List<string> _events = [];
    private readonly SettingsStore _settings = SettingsStore.InMemory(new UserSettings());

    private SmartCleanupPackManager NewManager(Func<OfflineModel, IProgress<long>, CancellationToken, Task>? download = null)
    {
        var manager = new SmartCleanupPackManager(_settings, _root, download ?? DownloadAsync, ReleaseAsync, () => TimeSpan.Zero);
        manager.InstalledChanged += () => _events.Add("installed");
        return manager;
    }

    private Task DownloadAsync(OfflineModel model, IProgress<long> progress, CancellationToken token)
    {
        _events.Add($"download {model.Directory}");
        string directory = model.PathUnder(_root);
        Directory.CreateDirectory(directory);
        foreach (OfflineModelFile file in model.Files)
        {
            File.WriteAllBytes(Path.Combine(directory, file.Local), [1]);
        }

        progress.Report(model.ApproxBytes);
        return Task.CompletedTask;
    }

    private Task ReleaseAsync()
    {
        _events.Add("release");
        return Task.CompletedTask;
    }

    [Fact]
    public void WithNothingOnDisk_NoModelRuns()
    {
        SmartCleanupPackManager manager = NewManager();

        Assert.Equal(new SmartCleanupPackState(null, null, null), manager.State);
        Assert.Null(manager.CurrentFolder());
    }

    [Fact]
    public async Task PickingATier_DownloadsItAndMakesItTheOneThatRuns()
    {
        SmartCleanupPackManager manager = NewManager();

        await manager.Pick(SmartCleanupTier.Standard);

        Assert.Equal(SmartCleanupTier.Standard, manager.State.Installed);
        Assert.Null(manager.State.Target);
        Assert.Equal(SmartCleanupTier.Standard, _settings.Current.Transcription.SmartCleanupTier);
        Assert.Equal(SmartCleanupPacks.Standard.PathUnder(_root), manager.CurrentFolder());
        Assert.Equal(["download llm-qwen3-1.7b", "installed"], _events);
    }

    [Fact]
    public async Task SwitchingTier_KeepsTheOldModelUntilTheNewOneIsComplete_ThenDeletesIt()
    {
        SmartCleanupPackManager manager = NewManager();
        await manager.Pick(SmartCleanupTier.Standard);
        _events.Clear();

        await manager.Pick(SmartCleanupTier.Better);

        Assert.Equal(["download llm-qwen3-4b", "release", "installed"], _events);
        Assert.False(Directory.Exists(SmartCleanupPacks.Standard.PathUnder(_root)));
        Assert.Equal(SmartCleanupTier.Better, manager.State.Installed);
    }

    [Fact]
    public async Task WhileTheOtherTierDownloads_TheInstalledOneKeepsRunning()
    {
        SmartCleanupPackManager manager = NewManager();
        await manager.Pick(SmartCleanupTier.Standard);
        TaskCompletionSource hold = new();
        SmartCleanupPackManager slow = NewManager(async (model, progress, token) =>
        {
            await hold.Task;
            await DownloadAsync(model, progress, token);
        });

        Task pick = slow.Pick(SmartCleanupTier.Better);

        Assert.Equal(SmartCleanupTier.Better, slow.State.Target);
        Assert.Equal(SmartCleanupTier.Standard, slow.State.Installed);
        Assert.Equal(SmartCleanupPacks.Standard.PathUnder(_root), slow.CurrentFolder());

        hold.SetResult();
        await pick;
        Assert.Equal(SmartCleanupTier.Better, slow.State.Installed);
    }

    [Fact]
    public async Task Cancel_DiscardsTheHalfDownloadedModel()
    {
        TaskCompletionSource hold = new();
        SmartCleanupPackManager manager = NewManager(async (model, progress, token) =>
        {
            Directory.CreateDirectory(model.PathUnder(_root));
            File.WriteAllBytes(Path.Combine(model.PathUnder(_root), "model.gguf.download"), [1]);
            await hold.Task.WaitAsync(token);
        });

        Task pick = manager.Pick(SmartCleanupTier.Better);
        manager.Cancel();
        hold.TrySetCanceled();
        await pick;

        Assert.Null(manager.State.Target);
        Assert.False(Directory.Exists(SmartCleanupPacks.Better.PathUnder(_root)));
    }

    [Fact]
    public async Task Remove_DeletesEveryModel_AndTurnsSmartCleanupOff()
    {
        SmartCleanupPackManager manager = NewManager();
        await manager.Pick(SmartCleanupTier.Better);
        _events.Clear();

        await manager.Remove();

        Assert.Null(manager.State.Installed);
        Assert.False(_settings.Current.Transcription.SmartCleanup);
        Assert.False(Directory.Exists(SmartCleanupPacks.Better.PathUnder(_root)));
        Assert.Equal(["release", "installed"], _events);
    }

    [Fact]
    public void TheCatalog_PinsEveryFileToAnExactHash()
    {
        foreach (OfflineModel model in new[] { SmartCleanupPacks.Standard, SmartCleanupPacks.Better })
        {
            Assert.All(model.Files, file =>
            {
                Assert.Equal(64, file.Sha256!.Length);
                Assert.StartsWith("https://", file.Url);
            });
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
