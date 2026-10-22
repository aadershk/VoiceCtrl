using System.IO;
using System.Media;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Dictation;

/// <summary>
/// The Warm Sound Cues (research 11): four short WAV files rendered once from the approved
/// prototype's synthesis (<c>ui/scripts/render-sound-cues.mjs</c>) and embedded in the exe. Played asynchronously
/// at VoiceCtrl's own volume in the Windows mixer, only while the setting is on. The engine asks
/// for every cue; this is where the setting is honoured.
/// </summary>
internal sealed class WavSoundCues : ISoundCues, IDisposable
{
    private readonly Func<bool> _enabled;
    private readonly Dictionary<SoundCue, SoundPlayer> _players = [];

    /// <param name="enabled">Read at each cue, so the Settings switch applies at once.</param>
    public WavSoundCues(Func<bool> enabled)
    {
        _enabled = enabled;
        foreach (SoundCue cue in Enum.GetValues<SoundCue>())
        {
            string name = $"VoiceCtrl.Sounds.{cue.ToString().ToLowerInvariant()}.wav";
            if (typeof(WavSoundCues).Assembly.GetManifestResourceStream(name) is not { } stream)
            {
                SimpleFileLogger.LogInfo($"Sound Cues: {name} is missing from the build");
                continue;
            }

            // Loaded up front: SoundPlayer would otherwise read the stream on the first Play,
            // on the thread that asked, which is the UI thread just as recording starts.
            var player = new SoundPlayer(stream);
            player.Load();
            _players[cue] = player;
        }
    }

    // Long enough for the start cue to finish: one SoundPlayer playing cuts off another.
    private static readonly TimeSpan PreviewGap = TimeSpan.FromMilliseconds(650);

    public void Play(SoundCue cue)
    {
        if (_enabled())
        {
            PlayNow(cue);
        }
    }

    /// <summary>Settings' Play: how a Dictation sounds (start, then done), even while the switch
    /// is off, so the cues can be heard before turning them on.</summary>
    public void PlayPreview()
    {
        PlayNow(SoundCue.Start);
        _ = Task.Delay(PreviewGap).ContinueWith(_ => PlayNow(SoundCue.Done), TaskScheduler.Default);
    }

    private void PlayNow(SoundCue cue)
    {
        if (!_players.TryGetValue(cue, out SoundPlayer? player))
        {
            return;
        }

        try
        {
            player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // No output device, or the device went away: a Dictation never fails over a cue.
            SimpleFileLogger.LogError("Sound Cue", ex);
        }
    }

    public void Dispose()
    {
        foreach (SoundPlayer player in _players.Values)
        {
            player.Stream?.Dispose();
            player.Dispose();
        }
    }
}
