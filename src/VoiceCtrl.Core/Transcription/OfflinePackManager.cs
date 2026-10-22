using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Setup;

namespace VoiceCtrl.Core.Transcription;

/// <summary>The Offline models row: the installed pack, and the pack being downloaded, if any.</summary>
/// <param name="Installed">The pack Offline uses now; <see cref="OfflinePack.None"/> is English only.
/// A pack whose files have gone missing counts as not installed.</param>
/// <param name="Target">The pack being downloaded, or whose download stopped.</param>
/// <param name="Download">Progress of <paramref name="Target"/>'s download.</param>
public sealed record OfflinePackState(OfflinePack Installed, OfflinePack? Target, ModelDownloadStatus? Download);

/// <summary>
/// Installs, replaces and removes the one extra Offline language pack (spec §5.3). Picking a pack
/// downloads it while the current one keeps working; when it is complete the setting switches and
/// the old pack's files go. Cancel discards a half-downloaded pack, but a download that
/// <i>failed</i> keeps its partial files, so trying again resumes. Everything is testable without a
/// network: the download itself is passed in.
/// </summary>
public sealed class OfflinePackManager
{
    private readonly SettingsStore _settings;
    private readonly string _modelsRoot;
    private readonly Func<OfflineModel, IProgress<long>, CancellationToken, Task> _download;
    private readonly Func<Task> _releaseModel;
    private readonly Func<TimeSpan> _clock;
    private readonly object _gate = new();
    private ActiveDownload? _active;

    /// <param name="modelsRoot">Where model folders live (<c>UserDataPaths.Models</c>).</param>
    /// <param name="download">Downloads what is missing of a model into its folder, reporting bytes.</param>
    /// <param name="releaseModel">Lets go of the loaded recognizer before a pack's files are
    /// deleted; Windows will not delete a model that is still open.</param>
    /// <param name="clock">A monotonic clock, for the download rate.</param>
    public OfflinePackManager(
        SettingsStore settings,
        string modelsRoot,
        Func<OfflineModel, IProgress<long>, CancellationToken, Task> download,
        Func<Task> releaseModel,
        Func<TimeSpan> clock)
    {
        _settings = settings;
        _modelsRoot = modelsRoot;
        _download = download;
        _releaseModel = releaseModel;
        _clock = clock;

        // A pack left complete but unused (a replaced pack whose delete failed, say) is dead weight.
        // Partial folders stay, for a download to resume.
        foreach (OfflinePackInfo info in OfflinePackCatalog.All)
        {
            if (info.Pack != Installed && IsComplete(info.Pack))
            {
                DeleteFolder(info.Pack);
            }
        }
    }

    /// <summary>The state or a download's progress changed. Raised on the thread that caused it.</summary>
    public event Action? Changed;

    /// <summary>The pack Offline uses changed: it is time to load the new model.</summary>
    public event Action? InstalledChanged;

    public OfflinePackState State
    {
        get
        {
            lock (_gate)
            {
                return new OfflinePackState(Installed, _active?.Pack, _active?.Download.Status);
            }
        }
    }

    /// <summary>The model Offline should run now: the installed extra pack, else English.</summary>
    public OfflineModel CurrentModel() => OfflineModels.For(Installed);

    private OfflinePack Installed
    {
        get
        {
            OfflinePack chosen = _settings.Current.Transcription.OfflinePack;
            return chosen != OfflinePack.None && IsComplete(chosen) ? chosen : OfflinePack.None;
        }
    }

    /// <summary>
    /// Makes <paramref name="pack"/> the installed pack: a no-op when it already is, a removal of the
    /// extra pack for <see cref="OfflinePack.None"/>, otherwise a download. The task settles when
    /// this pick has (the page does not wait on it; progress arrives through <see cref="Changed"/>).
    /// </summary>
    public async Task Pick(OfflinePack pack)
    {
        if (!Enum.IsDefined(pack))
        {
            return;
        }

        CancelActive();

        if (pack == Installed)
        {
            return;
        }

        if (pack == OfflinePack.None)
        {
            await SwitchToAsync(OfflinePack.None).ConfigureAwait(false);
            return;
        }

        OfflineModel model = OfflineModels.For(pack);
        string directory = FolderOf(pack);
        ActiveDownload active = new(pack, new ModelDownload(
            (progress, token) => _download(model, progress, token),
            () => LocalModelDownloader.IsFullyDownloaded(directory, model),
            () => LocalModelDownloader.BytesOnDisk(directory, model),
            model.ApproxBytes,
            _clock));
        active.Download.Changed += _ => Changed?.Invoke();

        lock (_gate)
        {
            _active = active;
        }

        Changed?.Invoke();
        await active.Download.Start().ConfigureAwait(false);

        bool finished;
        bool discard;
        lock (_gate)
        {
            // A Cancel or a newer pick took over while it ran: their state stands.
            finished = _active == active && active.Download.Status.State == ModelDownloadState.Done;
            if (finished)
            {
                _active = null;
            }

            // Only once the download has settled are its files closed and deletable. Not when a
            // newer pick of the same pack is already writing into the folder.
            discard = active.Discarded && _active?.Pack != pack;
        }

        if (finished)
        {
            await SwitchToAsync(pack).ConfigureAwait(false);
        }
        else if (discard && pack != Installed)
        {
            DeleteFolder(pack);
        }
    }

    /// <summary>The row's Cancel: stops the download and discards what it had fetched.</summary>
    public void Cancel()
    {
        if (CancelActive())
        {
            Changed?.Invoke();
        }
    }

    private bool CancelActive()
    {
        ActiveDownload? active;
        lock (_gate)
        {
            active = _active;
            _active = null;
        }

        if (active is null)
        {
            return false;
        }

        // The files are deleted by the pick that started the download, once it has settled: the
        // stream may still be open here, and Windows will not delete an open file.
        active.Discarded = true;
        active.Download.Stop();
        return true;
    }

    /// <summary>Saves the new pack, lets go of the loaded model, deletes the old pack's files, then
    /// tells the app to load the new one.</summary>
    private async Task SwitchToAsync(OfflinePack pack)
    {
        OfflinePack previous = _settings.Current.Transcription.OfflinePack;
        _settings.Update(s => s with { Transcription = s.Transcription with { OfflinePack = pack } });

        if (previous != OfflinePack.None && previous != pack)
        {
            try
            {
                await _releaseModel().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogError("Releasing the offline model", ex);
            }

            DeleteFolder(previous);
        }

        Changed?.Invoke();
        InstalledChanged?.Invoke();
    }

    private string FolderOf(OfflinePack pack) => OfflineModels.For(pack).PathUnder(_modelsRoot);

    private bool IsComplete(OfflinePack pack) =>
        LocalModelDownloader.IsFullyDownloaded(FolderOf(pack), OfflineModels.For(pack));

    private void DeleteFolder(OfflinePack pack)
    {
        string directory = FolderOf(pack);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Swept at the next start, once nothing has the model open.
            SimpleFileLogger.LogError($"Could not delete the {pack} language pack", ex);
        }
    }

    private sealed class ActiveDownload(OfflinePack pack, ModelDownload download)
    {
        public OfflinePack Pack { get; } = pack;

        public ModelDownload Download { get; } = download;

        /// <summary>Cancelled or replaced: what it fetched is to be deleted once it has settled.</summary>
        public bool Discarded { get; set; }
    }
}
