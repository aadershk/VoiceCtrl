using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Setup;

namespace VoiceCtrl.Core.Transcription;

/// <summary>Smart cleanup's model: the one that runs, and the one being downloaded, if any.</summary>
/// <param name="Installed">The tier that runs now, or null when no model is on disk.</param>
/// <param name="Target">The tier being downloaded, or whose download stopped.</param>
/// <param name="Download">Progress of <paramref name="Target"/>'s download.</param>
public sealed record SmartCleanupPackState(SmartCleanupTier? Installed, SmartCleanupTier? Target, ModelDownloadStatus? Download);

/// <summary>
/// Installs, replaces and removes Smart cleanup's language model, the way
/// <see cref="OfflinePackManager"/> does for the speech packs: picking a tier downloads it while the
/// current one keeps working, and when it is complete the setting switches and the old one's files
/// go. Cancel discards a half-downloaded model, but a download that <i>failed</i> keeps its partial
/// files so trying again resumes. The download itself is passed in, so nothing here needs a network.
/// </summary>
public sealed class SmartCleanupPackManager
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
    /// <param name="releaseModel">Ends the running <c>llama-server</c> before its files are deleted;
    /// Windows will not delete a model that is still open.</param>
    public SmartCleanupPackManager(
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
    }

    /// <summary>The state or a download's progress changed. Raised on the thread that caused it.</summary>
    public event Action? Changed;

    /// <summary>The model that runs changed: it is time to load the new one, or to stop.</summary>
    public event Action? InstalledChanged;

    public SmartCleanupPackState State
    {
        get
        {
            lock (_gate)
            {
                return new SmartCleanupPackState(Installed, _active?.Tier, _active?.Download.Status);
            }
        }
    }

    /// <summary>The folder of the model that runs now, or null when none is on disk.</summary>
    public string? CurrentFolder() => Installed is { } tier ? FolderOf(tier) : null;

    /// <summary>The tier Settings chose when its files are all there; otherwise whichever tier is
    /// complete, so a model keeps working while the other one downloads.</summary>
    private SmartCleanupTier? Installed
    {
        get
        {
            SmartCleanupTier chosen = _settings.Current.Transcription.SmartCleanupTier;
            if (IsComplete(chosen))
            {
                return chosen;
            }

            SmartCleanupTier other = chosen == SmartCleanupTier.Standard ? SmartCleanupTier.Better : SmartCleanupTier.Standard;
            return IsComplete(other) ? other : null;
        }
    }

    /// <summary>
    /// Makes <paramref name="tier"/> the model Smart cleanup runs: a no-op when it already is, a
    /// download otherwise. The task settles when this pick has (the page does not wait on it;
    /// progress arrives through <see cref="Changed"/>).
    /// </summary>
    public async Task Pick(SmartCleanupTier tier)
    {
        if (!Enum.IsDefined(tier))
        {
            return;
        }

        CancelActive();

        if (IsComplete(tier))
        {
            await SwitchToAsync(tier).ConfigureAwait(false);
            return;
        }

        OfflineModel model = SmartCleanupPacks.For(tier);
        string directory = FolderOf(tier);
        ActiveDownload active = new(tier, new ModelDownload(
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

            discard = active.Discarded && _active?.Tier != tier;
        }

        if (finished)
        {
            await SwitchToAsync(tier).ConfigureAwait(false);
        }
        else if (discard && Installed != tier)
        {
            DeleteFolder(tier);
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

    /// <summary>Deletes every Smart cleanup model and turns Smart cleanup off.</summary>
    public async Task Remove()
    {
        CancelActive();
        _settings.Update(s => s with { Transcription = s.Transcription with { SmartCleanup = false } });

        try
        {
            await _releaseModel().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Releasing the Smart cleanup model", ex);
        }

        foreach (SmartCleanupPackInfo info in SmartCleanupPacks.All)
        {
            DeleteFolder(info.Tier);
        }

        Changed?.Invoke();
        InstalledChanged?.Invoke();
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

    /// <summary>Saves the new tier, ends the running server, deletes the other tier's files, then
    /// tells the app to load the new one.</summary>
    private async Task SwitchToAsync(SmartCleanupTier tier)
    {
        _settings.Update(s => s with { Transcription = s.Transcription with { SmartCleanupTier = tier } });

        SmartCleanupTier other = tier == SmartCleanupTier.Standard ? SmartCleanupTier.Better : SmartCleanupTier.Standard;
        if (IsComplete(other))
        {
            try
            {
                await _releaseModel().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogError("Releasing the Smart cleanup model", ex);
            }

            DeleteFolder(other);
        }

        Changed?.Invoke();
        InstalledChanged?.Invoke();
    }

    private string FolderOf(SmartCleanupTier tier) => SmartCleanupPacks.For(tier).PathUnder(_modelsRoot);

    private bool IsComplete(SmartCleanupTier tier) =>
        LocalModelDownloader.IsFullyDownloaded(FolderOf(tier), SmartCleanupPacks.For(tier));

    private void DeleteFolder(SmartCleanupTier tier)
    {
        string directory = FolderOf(tier);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError($"Could not delete the {tier} Smart cleanup model", ex);
        }
    }

    private sealed class ActiveDownload(SmartCleanupTier tier, ModelDownload download)
    {
        public SmartCleanupTier Tier { get; } = tier;

        public ModelDownload Download { get; } = download;

        /// <summary>Cancelled or replaced: what it fetched is to be deleted once it has settled.</summary>
        public bool Discarded { get; set; }
    }
}
