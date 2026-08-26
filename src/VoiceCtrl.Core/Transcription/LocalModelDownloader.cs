using System.IO;
using System.Net.Http;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Downloads the offline Parakeet-TDT model files. Shared by LocalTranscriptionClient's lazy
/// first-dictation download and the first-run setup window's eager, progress-reporting download.
/// </summary>
public static class LocalModelDownloader
{
    private const string HuggingFaceRepo = "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
    private static readonly string[] ModelFileNames =
        ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];

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

    public static bool IsFullyDownloaded(string modelDirectory) =>
        ModelFileNames.All(fileName => File.Exists(Path.Combine(modelDirectory, fileName)));

    /// <summary>Clamped to 99 while a download is in progress: ApproxTotalBytes is only an
    /// estimate, so this must never be able to claim "100%/done" before DownloadAllAsync has
    /// actually returned.</summary>
    public static int ComputeDisplayPercent(long bytesSoFar)
    {
        int percent = (int)(bytesSoFar * 100 / ApproxTotalBytes);
        return Math.Clamp(percent, 0, 99);
    }

    public static async Task DownloadAllAsync(
        string modelDirectory,
        HttpClient httpClient,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(modelDirectory);

        long bytesSoFar = 0;

        foreach (string fileName in ModelFileNames)
        {
            string destinationPath = Path.Combine(modelDirectory, fileName);
            if (File.Exists(destinationPath))
            {
                continue;
            }

            SimpleFileLogger.LogInfo($"Downloading offline model file {fileName} (one-time, ~650-700MB total)...");

            string url = $"https://huggingface.co/{HuggingFaceRepo}/resolve/main/{fileName}";
            string tempPath = destinationPath + ".download";

            using (HttpResponseMessage response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                await using FileStream fileStream = File.Create(tempPath);
                await using Stream responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    bytesSoFar += bytesRead;
                    progress?.Report(bytesSoFar);
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            SimpleFileLogger.LogInfo($"Downloaded offline model file {fileName}.");
        }
    }
}
