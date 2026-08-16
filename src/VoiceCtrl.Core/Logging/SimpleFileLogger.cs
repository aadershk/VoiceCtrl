using System.IO;

namespace VoiceCtrl.Core.Logging;

/// <summary>
/// Persistent error log. Debug.WriteLine alone is compiled out of Release builds, so without this
/// a shipped build of this "runs unattended for weeks" background app has zero error visibility
/// once something goes wrong. Lives under LocalApplicationData rather than next to the exe, which
/// stays writable regardless of where the app is installed, and survives an exe-folder reinstall.
/// Each call opens/writes/closes rather than holding the file handle open, so a crash can never
/// leave a locked or partially-flushed log file behind.
/// </summary>
public static class SimpleFileLogger
{
    // Mutable (not readonly) so the test assembly can redirect it away from the real user log,
    // see VoiceCtrl.Core.Tests' module initializer. Without that seam, running the test suite
    // would interleave fake diagnostic entries into the same log.txt a real running instance
    // writes to, corrupting the exact evidence this file exists to provide.
    internal static string LogFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceCtrl",
        "log.txt");

    public static void LogError(string context, Exception ex) =>
        Write($"ERROR [{context}] {ex.GetType().Name}: {ex.Message}");

    public static void LogInfo(string message) =>
        Write($"INFO  {message}");

    private static void Write(string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogFilePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(LogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never be the reason this background app crashes or misbehaves. A
            // locked file or a permissions hiccup on the log directory should degrade silently.
        }
    }
}
