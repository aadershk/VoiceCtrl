using System.IO;
using System.Text.Json;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Audio;

/// <summary>One app's audio session on a playback device, and its volume (0..1).</summary>
/// <param name="Id">Stable while the session exists, and across a restart of VoiceCtrl (not of the
/// app playing).</param>
public sealed record AudioSessionLevel(string Id, int ProcessId, float Volume);

/// <summary>The per-app volumes Windows keeps (the Volume mixer). Implemented over Core Audio in
/// <see cref="CoreAudioSessionMixer"/>, and faked in tests.</summary>
public interface IAudioSessionMixer
{
    /// <summary>Every audio session on every active playback device, now.</summary>
    IReadOnlyList<AudioSessionLevel> Sessions();

    /// <summary>Sets each named session's volume. Sessions that have gone are skipped.</summary>
    void SetVolumes(IReadOnlyDictionary<string, float> volumes);
}

/// <summary>
/// Mute Other Audio (CONTEXT; spec §5.3). While a Dictation records, every other app's audio drops
/// to 20% of its level, then comes back. The levels are written to <c>ducking.json</c> before
/// anything changes, so if VoiceCtrl dies mid-Dictation the next start finds the file and puts
/// them back (<see cref="RestoreAfterCrash"/>).
/// <para>
/// Never throws: a Dictation must go ahead even if Windows' audio service is restarting. Not
/// thread-safe; the host runs every call on one queue.
/// </para>
/// </summary>
public sealed class AudioDucker(IAudioSessionMixer mixer, string stateFile, int ownProcessId, Func<bool> enabled) : IAudioDucker
{
    /// <summary>Other apps keep this share of their level: quiet enough not to cover the speaker
    /// or be picked up by the microphone, loud enough to follow a call.</summary>
    public const float DuckedShare = 0.2f;

    // Floats read back from Windows aren't bit-exact; a level within this of the one set counts
    // as untouched by the user.
    private const float SameLevel = 0.01f;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DuckedSessions? _ducked;

    /// <summary>Mute Other Audio as the host builds it: the real mixer, the file under the data root.</summary>
    public static AudioDucker CreateDefault(Func<bool> enabled) =>
        new(new CoreAudioSessionMixer(), Path.Combine(UserDataPaths.Root, "ducking.json"), Environment.ProcessId, enabled);

    public void Duck()
    {
        // A second Duck without a Restore would save already-lowered levels as the ones to go back to.
        if (_ducked is not null || !enabled())
        {
            return;
        }

        try
        {
            List<DuckedSession> sessions = mixer.Sessions()
                .Where(s => s.ProcessId != ownProcessId && s.Volume > 0)
                .Select(s => new DuckedSession(s.Id, s.Volume, s.Volume * DuckedShare))
                .ToList();
            if (sessions.Count == 0)
            {
                return;
            }

            var ducked = new DuckedSessions(sessions);

            // Saved first: if VoiceCtrl dies after this, the next start can put the levels back.
            AtomicFile.WriteAllBytes(stateFile, JsonSerializer.SerializeToUtf8Bytes(ducked, Json));
            _ducked = ducked;
            mixer.SetVolumes(sessions.ToDictionary(s => s.Id, s => s.Ducked));
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Mute Other Audio", ex);
        }
    }

    /// <summary>Puts back what <see cref="Duck"/> lowered, whether or not the setting is still on.</summary>
    public void Restore()
    {
        if (_ducked is not { } ducked)
        {
            return;
        }

        _ducked = null;
        RestoreFrom(ducked);
    }

    /// <summary>Call once at start, before any Dictation: a <c>ducking.json</c> left behind means
    /// the last run died while other apps were lowered.</summary>
    public void RestoreAfterCrash()
    {
        if (!File.Exists(stateFile))
        {
            return;
        }

        DuckedSessions? left = null;
        try
        {
            left = JsonSerializer.Deserialize<DuckedSessions>(File.ReadAllBytes(stateFile), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("Mute Other Audio: leftover levels", ex);
        }

        SimpleFileLogger.LogInfo("Mute Other Audio: putting levels back after an unfinished run");
        if (left is { Sessions: not null })
        {
            RestoreFrom(left);
        }
        else
        {
            DeleteStateFile();
        }
    }

    private void RestoreFrom(DuckedSessions ducked)
    {
        try
        {
            Dictionary<string, float> now = mixer.Sessions().ToDictionary(s => s.Id, s => s.Volume);

            // A level that no longer matches what was set was changed by the user meanwhile: theirs wins.
            Dictionary<string, float> back = ducked.Sessions
                .Where(s => now.TryGetValue(s.Id, out float level) && Math.Abs(level - s.Ducked) <= SameLevel)
                .ToDictionary(s => s.Id, s => s.Volume);
            if (back.Count > 0)
            {
                mixer.SetVolumes(back);
            }
        }
        catch (Exception ex)
        {
            // Kept on disk: the next start tries again.
            SimpleFileLogger.LogError("Mute Other Audio", ex);
            return;
        }

        DeleteStateFile();
    }

    private void DeleteStateFile()
    {
        try
        {
            File.Delete(stateFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("Mute Other Audio", ex);
        }
    }

    /// <summary>ducking.json: each lowered session, its level before, and the level set.</summary>
    private sealed record DuckedSessions(IReadOnlyList<DuckedSession> Sessions);

    private sealed record DuckedSession(string Id, float Volume, float Ducked);
}
