using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Downloads an offline model's files: the English model by default, or any <see cref="OfflineModel"/>
/// (the language packs). Shared by LocalTranscriptionClient's lazy first-dictation download, the
/// first-run setup window's eager download and Settings' pack downloads, all with progress.
/// </summary>
public static class LocalModelDownloader
{

    // Matches the "~650-700MB" figure already quoted in the README and the old setup console
    // banner. Only used to compute a display percentage, never trusted for correctness: the
    // real files can end up slightly larger or smaller than this as the model is updated
    // upstream, so ComputeDisplayPercent below is clamped and DownloadAllAsync itself is the
    // only thing allowed to signal "done".
    public const long ApproxTotalBytes = 700L * 1024 * 1024;

    /// <summary>Where the default-variant model lives, computed without an AppConfig. First-run
    /// setup needs this before any config has been loaded.</summary>
    public static string DefaultModelDirectory =>
        Path.Combine(UserDataPaths.Models, ConfigLoader.DefaultLocalModelVariant);

    public static bool IsFullyDownloaded(string modelDirectory, OfflineModel? model = null) =>
        (model ?? OfflineModels.English).Files.All(file => File.Exists(Path.Combine(modelDirectory, file.Local)));

    /// <summary>Clamped to 99 while a download is in progress: ApproxTotalBytes is only an
    /// estimate, so this must never be able to claim "100%/done" before DownloadAllAsync has
    /// actually returned.</summary>
    public static int ComputeDisplayPercent(long bytesSoFar)
    {
        int percent = (int)(bytesSoFar * 100 / ApproxTotalBytes);
        return Math.Clamp(percent, 0, 99);
    }

    /// <summary>A connection that sends nothing for this long counts as dropped.</summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(30);

    // One download at a time: Setup's download and a Dictation's lazy one write the same files.
    // The second caller waits, then finds the files there and returns.
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);

    /// <summary>What is already on disk, finished files plus any partial one, so a stopped download
    /// can show where it stopped before it resumes.</summary>
    public static long BytesOnDisk(string modelDirectory, OfflineModel? model = null) =>
        (model ?? OfflineModels.English).Files.Sum(file =>
        {
            string path = Path.Combine(modelDirectory, file.Local);
            return FileLength(path) ?? FileLength(path + ".download") ?? 0;
        });

    /// <summary>
    /// Downloads whatever is missing. A partial <c>.download</c> file left by a stopped or failed
    /// run resumes with an HTTP Range request (spec §7 Setup); a server that ignores the range
    /// sends the whole file, which then starts over. <paramref name="progress"/> gets the bytes
    /// on disk across all files, so it continues from where a stopped download left off.
    /// </summary>
    /// <exception cref="TimeoutException">The connection sent nothing for <paramref name="stallTimeout"/>.</exception>
    public static async Task DownloadAllAsync(
        string modelDirectory,
        HttpClient httpClient,
        IProgress<long>? progress,
        CancellationToken cancellationToken,
        TimeSpan? stallTimeout = null,
        OfflineModel? model = null)
    {
        model ??= OfflineModels.English;
        await DownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(modelDirectory);
            long bytesSoFar = BytesOnDisk(modelDirectory, model);

            foreach (OfflineModelFile file in model.Files)
            {
                string destinationPath = Path.Combine(modelDirectory, file.Local);
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                bytesSoFar = await DownloadFileAsync(
                    model.Repo, file, destinationPath, bytesSoFar, httpClient, progress, stallTimeout ?? DefaultStallTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    private static async Task<long> DownloadFileAsync(
        string repo,
        OfflineModelFile file,
        string destinationPath,
        long bytesSoFar,
        HttpClient httpClient,
        IProgress<long>? progress,
        TimeSpan stallTimeout,
        CancellationToken cancellationToken)
    {
        string fileName = file.Remote;
        string url = file.Url ?? $"https://huggingface.co/{repo}/resolve/main/{fileName}";
        string tempPath = destinationPath + ".download";
        long partial = FileLength(tempPath) ?? 0;

        SimpleFileLogger.LogInfo(partial > 0
            ? $"Resuming offline model file {fileName} from {partial} bytes..."
            : $"Downloading offline model file {fileName} (one-time)...");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (partial > 0)
        {
            request.Headers.Range = new RangeHeaderValue(partial, null);
        }

        using (HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false))
        {
            // The partial file already holds the whole file: the last run stopped just before the rename.
            if (partial > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Move(tempPath, destinationPath, overwrite: true);
                return bytesSoFar;
            }

            response.EnsureSuccessStatusCode();

            bool resumed = partial > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed)
            {
                bytesSoFar -= partial;
            }

            await using FileStream fileStream = new(tempPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
            await using Stream responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await ReadWithStallTimeoutAsync(responseStream, buffer, stallTimeout, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                bytesSoFar += bytesRead;
                progress?.Report(bytesSoFar);
            }
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        if (file.Sha256 is { } expected && !await HashMatchesAsync(destinationPath, expected, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(destinationPath);
            throw new InvalidDataException($"The downloaded file {fileName} does not match its expected checksum.");
        }

        SimpleFileLogger.LogInfo($"Downloaded offline model file {fileName}.");
        return bytesSoFar;
    }

    private static async Task<bool> HashMatchesAsync(string path, string expectedHex, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> ReadWithStallTimeoutAsync(Stream stream, byte[] buffer, TimeSpan stallTimeout, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(stallTimeout);
        try
        {
            return await stream.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The model download sent nothing for {stallTimeout.TotalSeconds:0} s.");
        }
    }

    private static long? FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : null;
}
