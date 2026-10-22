using System.IO;
using System.Text.Json;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Updates;

/// <summary>The first start after an update from <see cref="From"/> to <see cref="To"/> never finished.</summary>
public sealed record FailedUpdate(string From, string To);

/// <summary>
/// The failed-update guard (spec §9, research 01 §3). Velopack has no rollback, so the app writes
/// a "starting" marker before WebView2 and the hooks come up and deletes it once it is running.
/// A marker still there on the next start means that start died; if it was the first start after
/// an update, the app offers the previous version. Once a start after the update finishes, later
/// failures are no longer blamed on it.
/// </summary>
public sealed class StartupMarker(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Call before anything risky, with the version that ran last (before this start records its
    /// own). Returns the update to offer undoing, or null.
    /// </summary>
    public FailedUpdate? Begin(string currentVersion, string? lastRunVersion)
    {
        Marker? left = Read();
        FailedUpdate? failed = null;
        string? updatedFrom;
        if (left is { UpdatedFrom: { } from } && left.Version == currentVersion)
        {
            failed = new FailedUpdate(from, currentVersion);
            updatedFrom = from;
        }
        else
        {
            updatedFrom = lastRunVersion is not null && AppVersion.Compare(currentVersion, lastRunVersion) > 0 ? lastRunVersion : null;
        }

        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new Marker(currentVersion, updatedFrom), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("Startup marker", ex);
        }

        return failed;
    }

    /// <summary>The app is up: this start counts as a success.</summary>
    public void Complete()
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("Startup marker", ex);
        }
    }

    private Marker? Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <param name="UpdatedFrom">Set only while no start of <paramref name="Version"/> has finished
    /// since it replaced this older version.</param>
    private sealed record Marker(string Version, string? UpdatedFrom);
}
