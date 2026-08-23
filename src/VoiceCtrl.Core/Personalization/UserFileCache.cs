using System.IO;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// Holds the parsed form of a small user-editable file and reparses it when the file changes on
/// disk. Staleness is checked by stat rather than by a FileSystemWatcher: the check happens once
/// per transcription against three files, which is microseconds, and it has none of the watcher's
/// failure modes (missed events on network paths, a handle held open on a directory the user may
/// want to move, an event arriving mid-write while the editor has only flushed half the file).
/// </summary>
public sealed class UserFileCache<T>
{
    private readonly string _path;
    private readonly Func<string, T> _parse;
    private readonly T _fallback;
    private readonly object _gate = new();

    private (DateTime WriteTimeUtc, long Length)? _loadedStamp;
    private T _value;

    public UserFileCache(string path, Func<string, T> parse, T fallback)
    {
        _path = path;
        _parse = parse;
        _fallback = fallback;
        _value = fallback;
    }

    public T Value
    {
        get
        {
            lock (_gate)
            {
                (DateTime WriteTimeUtc, long Length)? current = ReadStamp();
                if (current != _loadedStamp)
                {
                    _value = Load();
                    _loadedStamp = current;
                }

                return _value;
            }
        }
    }

    private (DateTime WriteTimeUtc, long Length)? ReadStamp()
    {
        try
        {
            var info = new FileInfo(_path);

            // Length as well as timestamp: an edit that lands inside the same filesystem timestamp
            // tick is unlikely but free to rule out, and a truncation is exactly the kind of change
            // that would otherwise be missed.
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private T Load()
    {
        try
        {
            return File.Exists(_path) ? _parse(File.ReadAllText(_path)) : _fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Most likely the user's editor holding the file open mid-save. Keeping the fallback
            // means this dictation loses the personalisation rather than failing outright, and the
            // next one picks the file up once the write completes.
            SimpleFileLogger.LogInfo($"Could not read {Path.GetFileName(_path)}, using defaults: {ex.Message}");
            return _fallback;
        }
    }
}
