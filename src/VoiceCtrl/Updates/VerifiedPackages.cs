using System.IO;
using Velopack;
using UpdateInfo = Velopack.UpdateInfo;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Updates;
using static VoiceCtrl.Updates.UpdateFeed;

namespace VoiceCtrl.Updates;

/// <summary>
/// Velopack downloads and installs; this makes sure nothing it installs is unsigned (spec §9).
/// A download is kept only when the release's signed manifest names it byte for byte, and the
/// manifest is kept beside it so the next start can check both again before installing. Velopack's
/// own apply-on-start is off (<c>InstallerHooks</c>), so a package that was never checked, say
/// because the app died mid-download, is deleted rather than installed.
/// </summary>
internal static class VerifiedPackages
{
    /// <summary>
    /// Checks the manifest, downloads the full package, checks it against the manifest. Throws
    /// <see cref="UpdateVerificationException"/> (after deleting the package) when anything
    /// doesn't match; other exceptions are the network's.
    /// </summary>
    public static async Task DownloadAsync(PackageManager manager, UpdateFeed feed, UpdateInfo update, Action<int> progress, CancellationToken cancellationToken)
    {
        VelopackAsset target = update.TargetFullRelease;
        string version = target.Version.ToString();

        // First, so a release without a valid manifest isn't even downloaded.
        byte[] manifestFile = await feed.FetchManifestAsync(version, cancellationToken).ConfigureAwait(false);
        ReleaseManifest manifest = ReleaseSigning.Verify(manifestFile, ReleaseSigning.PublicKey, version);

        await manager.DownloadUpdatesAsync(update, progress, cancellationToken).ConfigureAwait(false);

        string package = Path.Combine(manager.PackagesDir, target.FileName);
        try
        {
            ReleaseSigning.VerifyPackage(manifest, package);
        }
        catch (UpdateVerificationException)
        {
            Discard(manager, target);
            throw;
        }

        await File.WriteAllBytesAsync(ManifestPath(manager, version), manifestFile, cancellationToken).ConfigureAwait(false);
        SimpleFileLogger.LogInfo($"Updates: {version} downloaded and verified");
    }

    /// <summary>
    /// The downloaded update to install at this start, when its kept manifest still vouches for
    /// it. Anything else waiting is deleted, so it can't be installed later either.
    /// </summary>
    public static VelopackAsset? Pending(PackageManager manager)
    {
        if (!manager.IsInstalled || manager.UpdatePendingRestart is not { } pending)
        {
            return null;
        }

        string version = pending.Version.ToString();
        try
        {
            byte[] manifestFile = File.ReadAllBytes(ManifestPath(manager, version));
            ReleaseSigning.VerifyPackage(
                ReleaseSigning.Verify(manifestFile, ReleaseSigning.PublicKey, version),
                Path.Combine(manager.PackagesDir, pending.FileName));
            return pending;
        }
        catch (Exception ex) when (ex is UpdateVerificationException or IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError($"Updates: {version} is waiting but not verified, deleting it", ex);
            Discard(manager, pending);
            return null;
        }
    }

    /// <summary>Deletes a downloaded package and its manifest.</summary>
    public static void Discard(PackageManager manager, VelopackAsset package)
    {
        DeleteIfThere(Path.Combine(manager.PackagesDir, package.FileName));
        DeleteIfThere(ManifestPath(manager, package.Version.ToString()));
    }

    private static string ManifestPath(PackageManager manager, string version) =>
        Path.Combine(manager.PackagesDir, ReleaseSigning.ManifestFileName(version));

    private static void DeleteIfThere(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError($"Updates: couldn't delete {Path.GetFileName(path)}", ex);
        }
    }
}
