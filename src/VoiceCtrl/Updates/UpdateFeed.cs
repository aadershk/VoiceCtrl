using System.IO;
using System.Net.Http;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;
using IUpdateSource = Velopack.Sources.IUpdateSource;
using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Updates;

/// <summary>
/// Where updates and their signed manifests come from (spec §9): stable GitHub releases of the
/// public repo. <c>VOICECTRL_UPDATE_FEED</c> points it at a local folder instead, for the release
/// workflow's two-version check. That override can't install anything the release key didn't
/// sign, so it is allowed in Release builds.
/// </summary>
internal sealed class UpdateFeed
{
    public const string RepoUrl = "https://github.com/aadershk/VoiceCtrl";
    private const string LocalFeedVariable = "VOICECTRL_UPDATE_FEED";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string? _localFolder = Environment.GetEnvironmentVariable(LocalFeedVariable) is { Length: > 0 } folder ? folder : null;

    /// <summary>For the regular checks: the newest stable release.</summary>
    public PackageManager Latest() =>
        new(_localFolder is null ? new GithubSource(RepoUrl, accessToken: null, prerelease: false) : new SimpleFileSource(new DirectoryInfo(_localFolder)),
            Options(allowDowngrade: false));

    /// <summary>For going back after a failed update: exactly <paramref name="version"/>, older or not.</summary>
    public PackageManager Pinned(string version) =>
        new(new PinnedSource(_localFolder is null ? new SimpleWebSource(ReleaseFolderUrl(version)) : new SimpleFileSource(new DirectoryInfo(_localFolder)), version),
            Options(allowDowngrade: true));

    /// <summary>The release's signed manifest, from the same place as the package.</summary>
    public async Task<byte[]> FetchManifestAsync(string version, CancellationToken cancellationToken)
    {
        string name = ReleaseSigning.ManifestFileName(version);
        if (_localFolder is not null)
        {
            return await File.ReadAllBytesAsync(Path.Combine(_localFolder, name), cancellationToken).ConfigureAwait(false);
        }

        return await Http.GetByteArrayAsync(new Uri(ReleaseFolderUrl(version) + name), cancellationToken).ConfigureAwait(false);
    }

    private static string ReleaseFolderUrl(string version) => $"{RepoUrl}/releases/download/v{version}/";

    // Deltas off: every update is the full package, byte for byte what the manifest's SHA-256
    // describes, rather than one rebuilt from deltas on this PC.
    private static UpdateOptions Options(bool allowDowngrade) =>
        new() { MaximumDeltasBeforeFallback = -1, AllowVersionDowngrade = allowDowngrade };

    /// <summary>Velopack's manager, plus where it keeps downloaded packages (protected there).</summary>
    public sealed class PackageManager(IUpdateSource source, UpdateOptions options) : UpdateManager(source, options)
    {
        /// <summary>Only meaningful when <see cref="UpdateManager.IsInstalled"/>.</summary>
        public string PackagesDir => Locator.PackagesDir!;
    }

    /// <summary>Hides every release but one, so a check offers exactly that version.</summary>
    private sealed class PinnedSource(IUpdateSource inner, string version) : IUpdateSource
    {
        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            VelopackAssetFeed feed = await inner.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease).ConfigureAwait(false);
            return new VelopackAssetFeed
            {
                Assets = feed.Assets.Where(a => a.Type == VelopackAssetType.Full && a.Version.ToString() == version).ToArray(),
            };
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default) =>
            inner.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
