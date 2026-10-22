using System.Text.Json;
using VoiceCtrl.Core.Audio;

namespace VoiceCtrl.Core.Tests.Audio;

/// <summary>Mute Other Audio (CONTEXT; spec §5.3): other apps go to 20% while recording and come
/// back afterwards, even after a crash.</summary>
public sealed class AudioDuckerTests : IDisposable
{
    private const int OwnProcess = 100;

    private readonly string _folder = Directory.CreateTempSubdirectory("voicectrl-ducking-").FullName;
    private readonly FakeMixer _mixer = new();
    private bool _enabled = true;

    private string StateFile => Path.Combine(_folder, "ducking.json");

    private AudioDucker NewDucker() => new(_mixer, StateFile, OwnProcess, () => _enabled);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Duck_LowersEveryOtherApp_To20PercentOfItsLevel()
    {
        _mixer.Add("spotify", processId: 1, volume: 1.0f);
        _mixer.Add("chrome", processId: 2, volume: 0.5f);
        _mixer.Add("voicectrl", processId: OwnProcess, volume: 1.0f);

        NewDucker().Duck();

        Assert.Equal(0.2f, _mixer.Volume("spotify"), 3);
        Assert.Equal(0.1f, _mixer.Volume("chrome"), 3);
        Assert.Equal(1.0f, _mixer.Volume("voicectrl"), 3);
    }

    [Fact]
    public void Restore_PutsEveryLevelBack_AndLeavesNoFile()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        _mixer.Add("chrome", processId: 2, volume: 0.5f);
        AudioDucker ducker = NewDucker();

        ducker.Duck();
        ducker.Restore();

        Assert.Equal(0.8f, _mixer.Volume("spotify"), 3);
        Assert.Equal(0.5f, _mixer.Volume("chrome"), 3);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public void Duck_SavesTheLevels_BeforeChangingAny()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        string? savedWhenChanged = null;
        _mixer.BeforeChange = () => savedWhenChanged = File.Exists(StateFile) ? File.ReadAllText(StateFile) : null;

        NewDucker().Duck();

        Assert.NotNull(savedWhenChanged);
        Assert.Contains("spotify", savedWhenChanged);
        Assert.Contains("0.8", savedWhenChanged);
    }

    [Fact]
    public void AfterACrash_TheNextStartPutsTheLevelsBack_AndDeletesTheFile()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        NewDucker().Duck();

        // The app died while recording: nothing called Restore. The next start makes a new ducker.
        NewDucker().RestoreAfterCrash();

        Assert.Equal(0.8f, _mixer.Volume("spotify"), 3);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public void AfterACleanRun_TheNextStartChangesNothing()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);

        NewDucker().RestoreAfterCrash();

        Assert.Equal(0.8f, _mixer.Volume("spotify"), 3);
        Assert.Equal(0, _mixer.Changes);
    }

    [Fact]
    public void ALevelTheUserChangedMeanwhile_IsLeftAsTheUserSetIt()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        _mixer.Add("chrome", processId: 2, volume: 0.5f);
        AudioDucker ducker = NewDucker();
        ducker.Duck();

        _mixer.Set("spotify", 0.6f);
        ducker.Restore();

        Assert.Equal(0.6f, _mixer.Volume("spotify"), 3);
        Assert.Equal(0.5f, _mixer.Volume("chrome"), 3);
    }

    [Fact]
    public void AnAppThatClosedMeanwhile_IsSkipped()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        _mixer.Add("chrome", processId: 2, volume: 0.5f);
        AudioDucker ducker = NewDucker();
        ducker.Duck();

        _mixer.Remove("spotify");
        ducker.Restore();

        Assert.Equal(0.5f, _mixer.Volume("chrome"), 3);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public void AnAppThatStartedPlayingMeanwhile_IsNotTouched()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        AudioDucker ducker = NewDucker();
        ducker.Duck();

        _mixer.Add("teams", processId: 3, volume: 0.7f);
        ducker.Restore();

        Assert.Equal(0.7f, _mixer.Volume("teams"), 3);
    }

    [Fact]
    public void Off_ChangesNothing()
    {
        _enabled = false;
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        AudioDucker ducker = NewDucker();

        ducker.Duck();

        Assert.Equal(0.8f, _mixer.Volume("spotify"), 3);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public void TurnedOffWhileRecording_StillPutsTheLevelsBack()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        AudioDucker ducker = NewDucker();
        ducker.Duck();

        _enabled = false;
        ducker.Restore();

        Assert.Equal(0.8f, _mixer.Volume("spotify"), 3);
    }

    [Fact]
    public void SilentApps_AndNoOtherApps_WriteNoFile()
    {
        _mixer.Add("muted", processId: 1, volume: 0f);
        _mixer.Add("voicectrl", processId: OwnProcess, volume: 1.0f);

        NewDucker().Duck();

        Assert.False(File.Exists(StateFile));
        Assert.Equal(0, _mixer.Changes);
    }

    [Fact]
    public void ASecondDuck_BeforeRestore_KeepsTheFirstLevels()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        AudioDucker ducker = NewDucker();

        ducker.Duck();
        ducker.Duck();
        ducker.Restore();

        Assert.Equal(0.8f, _mixer.Volume("spotify"), 3);
    }

    [Fact]
    public void AMixerThatFails_NeverStopsADictation()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);
        _mixer.Throws = new InvalidOperationException("audio service restarting");
        AudioDucker ducker = NewDucker();

        ducker.Duck();
        ducker.Restore();
        ducker.RestoreAfterCrash();
    }

    [Fact]
    public void AnUnreadableLeftoverFile_IsDeleted()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.2f);
        File.WriteAllText(StateFile, "{ not json");

        NewDucker().RestoreAfterCrash();

        Assert.False(File.Exists(StateFile));
        Assert.Equal(0.2f, _mixer.Volume("spotify"), 3);
    }

    [Fact]
    public void TheFile_KeepsOnlyLevels()
    {
        _mixer.Add("spotify", processId: 1, volume: 0.8f);

        NewDucker().Duck();

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(StateFile));
        JsonElement session = saved.RootElement.GetProperty("sessions")[0];
        Assert.Equal("spotify", session.GetProperty("id").GetString());
        Assert.Equal(0.8f, session.GetProperty("volume").GetSingle(), 3);
        Assert.Equal(0.16f, session.GetProperty("ducked").GetSingle(), 3);
    }

    private sealed class FakeMixer : IAudioSessionMixer
    {
        private readonly Dictionary<string, (int ProcessId, float Volume)> _sessions = [];

        public Action? BeforeChange { get; set; }

        public Exception? Throws { get; set; }

        public int Changes { get; private set; }

        public void Add(string id, int processId, float volume) => _sessions[id] = (processId, volume);

        public void Remove(string id) => _sessions.Remove(id);

        public void Set(string id, float volume) => _sessions[id] = (_sessions[id].ProcessId, volume);

        public float Volume(string id) => _sessions[id].Volume;

        public IReadOnlyList<AudioSessionLevel> Sessions()
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            return _sessions.Select(s => new AudioSessionLevel(s.Key, s.Value.ProcessId, s.Value.Volume)).ToList();
        }

        public void SetVolumes(IReadOnlyDictionary<string, float> volumes)
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            BeforeChange?.Invoke();
            foreach ((string id, float volume) in volumes)
            {
                if (_sessions.ContainsKey(id))
                {
                    Set(id, volume);
                    Changes++;
                }
            }
        }
    }
}
