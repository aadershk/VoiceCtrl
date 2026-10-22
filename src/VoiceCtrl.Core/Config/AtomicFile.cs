using System.IO;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Writes and sets aside the files under the data root that must never be left half-written:
/// settings.json and secrets.bin. A crash or power cut mid-save leaves either the old file or the
/// new one, never a truncated mix that would read as corrupt on the next start.
/// </summary>
internal static class AtomicFile
{
    /// <summary>Writes to a sibling temp file, flushes it to disk, then swaps it in with
    /// <see cref="File.Replace(string, string, string?)"/>. Throws on IO failure; callers decide
    /// how much a failed save matters.</summary>
    public static void WriteAllBytes(string path, byte[] contents)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    /// <summary>Renames a file that can't be read to "&lt;name&gt;.&lt;label&gt;-&lt;time&gt;&lt;ext&gt;" next to it,
    /// so the user's data is kept for recovery rather than overwritten, and the app can start
    /// from defaults. Returns the new path, or null if even the rename failed.</summary>
    public static string? MoveAside(string path, string label)
    {
        string asidePath = Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(path)}.{label}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");

        try
        {
            File.Move(path, asidePath, overwrite: true);
            return asidePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
