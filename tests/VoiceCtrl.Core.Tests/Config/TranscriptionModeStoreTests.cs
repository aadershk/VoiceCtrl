using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class TranscriptionModeStoreTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void NoPrefsFile_NoExplicitEnvMode_SeedsToAuto()
    {
        AppConfig config = LoadConfig(envContents: null);

        TranscriptionModeStore store = TranscriptionModeStore.Load(TempPrefsPath(), config);

        Assert.Equal(TranscriptionModePreference.Auto, store.Current);
    }

    [Fact]
    public void NoPrefsFile_ExplicitOfflineEnvMode_SeedsToOffline()
    {
        AppConfig config = LoadConfig("TRANSCRIPTION_MODE=Offline");

        TranscriptionModeStore store = TranscriptionModeStore.Load(TempPrefsPath(), config);

        Assert.Equal(TranscriptionModePreference.Offline, store.Current);
    }

    [Fact]
    public void NoPrefsFile_ExplicitOnlineEnvMode_SeedsToAuto()
    {
        // Explicit Online still widens to Auto, since Auto behaves identically to Online whenever
        // there's internet, so this only ever adds a fallback, never removes existing behavior.
        AppConfig config = LoadConfig("TRANSCRIPTION_MODE=Online");

        TranscriptionModeStore store = TranscriptionModeStore.Load(TempPrefsPath(), config);

        Assert.Equal(TranscriptionModePreference.Auto, store.Current);
    }

    [Fact]
    public void SeedingPersistsImmediately_SoALaterEnvEditDoesNotChangeIt()
    {
        string prefsPath = TempPrefsPath();
        TranscriptionModeStore.Load(prefsPath, LoadConfig("TRANSCRIPTION_MODE=Offline"));

        // Simulate the user editing .env afterward for an unrelated reason.
        TranscriptionModeStore reloaded = TranscriptionModeStore.Load(prefsPath, LoadConfig(envContents: null));

        Assert.Equal(TranscriptionModePreference.Offline, reloaded.Current);
    }

    [Fact]
    public void ExistingPrefsFile_IsSoleSourceOfTruth_RegardlessOfEnv()
    {
        string prefsPath = TempPrefsPath();
        File.WriteAllText(prefsPath, """{"TranscriptionMode":"Online"}""");

        AppConfig config = LoadConfig("TRANSCRIPTION_MODE=Offline");
        TranscriptionModeStore store = TranscriptionModeStore.Load(prefsPath, config);

        Assert.Equal(TranscriptionModePreference.Online, store.Current);
    }

    [Fact]
    public void CorruptPrefsFile_FallsBackToSeedAndOverwritesIt()
    {
        string prefsPath = TempPrefsPath();
        File.WriteAllText(prefsPath, "{not valid json");

        TranscriptionModeStore store = TranscriptionModeStore.Load(prefsPath, LoadConfig(envContents: null));

        Assert.Equal(TranscriptionModePreference.Auto, store.Current);
        Assert.Contains("Auto", File.ReadAllText(prefsPath));
    }

    [Fact]
    public void Save_PersistsCurrentAcrossReload()
    {
        string prefsPath = TempPrefsPath();
        AppConfig config = LoadConfig(envContents: null);
        TranscriptionModeStore store = TranscriptionModeStore.Load(prefsPath, config);

        store.Current = TranscriptionModePreference.Offline;
        store.Save();

        TranscriptionModeStore reloaded = TranscriptionModeStore.Load(prefsPath, config);
        Assert.Equal(TranscriptionModePreference.Offline, reloaded.Current);
    }

    private AppConfig LoadConfig(string? envContents)
    {
        string envPath = Path.Combine(Path.GetTempPath(), $"voicectrl-test-{Guid.NewGuid()}.env");
        if (envContents is not null)
        {
            File.WriteAllText(envPath, envContents);
            _tempFiles.Add(envPath);
        }

        return ConfigLoader.Load(envPath);
    }

    private string TempPrefsPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voicectrl-test-prefs-{Guid.NewGuid()}.json");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
