using System.IO;
using VoiceCtrl.Core.Hotkey;
using Xunit;

namespace VoiceCtrl.Core.Tests.Hotkey;

public class HotkeySettingsTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void NoFile_LoadsShippedDefaults()
    {
        HotkeySettings settings = HotkeySettings.Load(TempPath());

        Assert.Equal(HotkeyKeyCatalog.DefaultHotkeyKey, settings.HotkeyKey);
        Assert.False(settings.TriggerEnabled);
        Assert.False(settings.IsTriggerReady);
    }

    [Fact]
    public void SingleKeyTrigger_PersistsAcrossReload()
    {
        string path = TempPath();
        var settings = new HotkeySettings
        {
            HotkeyKey = "Left Alt",
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeSingle,
            TriggerKey = "F5",
        };

        settings.Save(path);
        HotkeySettings reloaded = HotkeySettings.Load(path);

        Assert.Equal("Left Alt", reloaded.HotkeyKey);
        Assert.True(reloaded.TriggerEnabled);
        Assert.Equal("F5", reloaded.TriggerKey);
        Assert.True(reloaded.IsTriggerReady);
    }

    [Fact]
    public void ChordTrigger_PersistsAcrossReload()
    {
        string path = TempPath();
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeChord,
            ChordPrefix = "Left Ctrl",
            ChordSecondKeyVk = 0x20,
            ChordSecondKeyLabel = "Space",
        };

        settings.Save(path);
        HotkeySettings reloaded = HotkeySettings.Load(path);

        Assert.Equal("Left Ctrl", reloaded.ChordPrefix);
        Assert.Equal(0x20, reloaded.ChordSecondKeyVk);
        Assert.Equal("Space", reloaded.ChordSecondKeyLabel);
        Assert.True(reloaded.IsTriggerReady);
    }

    [Fact]
    public void TriggerEnabled_ButKeyNotYetChosen_IsNotReady()
    {
        var settings = new HotkeySettings { TriggerEnabled = true, TriggerMode = HotkeySettings.TriggerModeSingle };

        Assert.False(settings.IsTriggerReady);
    }

    [Fact]
    public void ChordMode_MissingSecondKey_IsNotReady()
    {
        var settings = new HotkeySettings
        {
            TriggerEnabled = true,
            TriggerMode = HotkeySettings.TriggerModeChord,
            ChordPrefix = "Left Ctrl",
        };

        Assert.False(settings.IsTriggerReady);
    }

    [Fact]
    public void MalformedFile_FallsBackToDefaults()
    {
        string path = TempPath();
        File.WriteAllText(path, "{ not valid json");

        HotkeySettings settings = HotkeySettings.Load(path);

        Assert.Equal(HotkeyKeyCatalog.DefaultHotkeyKey, settings.HotkeyKey);
        Assert.False(settings.TriggerEnabled);
    }

    private string TempPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voicectrl-test-hotkey-{Guid.NewGuid()}.json");
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
