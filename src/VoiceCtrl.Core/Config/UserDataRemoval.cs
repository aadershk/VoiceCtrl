using System.IO;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// "Also delete your data?" at uninstall (spec §9): removes the data root whole, which is
/// settings, secrets, history, Personalization, models, logs and the WebView2 cache. Velopack
/// stops the app before the hook runs, but a WebView2 process can hold its cache files a moment
/// longer, so locked files are retried for a short while before giving up on them.
/// </summary>
public static class UserDataRemoval
{
    private const string DataRootFolderName = "VoiceCtrl";
    private static readonly TimeSpan RetryStep = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Deletes <paramref name="root"/> and everything in it. Returns true once nothing is left
    /// (a root that never existed counts). Refuses, and returns false, for any folder that isn't
    /// named like the data root or the sandbox root: this deletes recursively, so a wrong path
    /// must never reach it. Never throws.
    /// </summary>
    /// <param name="wait">Called between attempts with the pause to take; tests pass a fake.</param>
    public static bool DeleteAll(string root, TimeSpan patience, Action<TimeSpan> wait)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!IsDataRoot(fullRoot))
        {
            return false;
        }

        TimeSpan waited = TimeSpan.Zero;
        while (true)
        {
            if (TryDeleteTree(fullRoot))
            {
                return true;
            }

            if (waited >= patience)
            {
                return false;
            }

            wait(RetryStep);
            waited += RetryStep;
        }
    }

    public static bool DeleteAll(string root, TimeSpan patience) =>
        DeleteAll(root, patience, Thread.Sleep);

    private static bool IsDataRoot(string fullRoot) =>
        string.Equals(Path.GetFileName(fullRoot), DataRootFolderName, StringComparison.OrdinalIgnoreCase) ||
        (UserDataPaths.SandboxRoot is { } sandbox &&
         string.Equals(fullRoot, Path.TrimEndingDirectorySeparator(sandbox), StringComparison.OrdinalIgnoreCase));

    /// <summary>One pass: every file it can delete, then every folder that is empty. A locked file
    /// is skipped rather than stopping the pass, so one busy cache file never keeps the rest.</summary>
    private static bool TryDeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return true;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                TryDeleteFile(file);
            }

            // Deepest first, so each folder is empty by the time it's reached.
            foreach (string folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                TryDeleteEmptyFolder(folder);
            }

            TryDeleteEmptyFolder(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The tree changed under the enumeration; the next pass starts over.
        }

        return !Directory.Exists(root);
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteEmptyFolder(string folder)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
