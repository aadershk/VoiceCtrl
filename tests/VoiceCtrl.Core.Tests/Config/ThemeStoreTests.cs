using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class ThemeStoreTests
{
    [Fact]
    public void AFreshInstall_FollowsSystem()
    {
        var store = new ThemeStore(SettingsStore.InMemory(new UserSettings()));

        Assert.Equal(ThemePreference.System, store.Current);
    }

    [Fact]
    public void SetAndSave_WritesTheThemeIntoTheSettingsStore()
    {
        SettingsStore settings = SettingsStore.InMemory(new UserSettings());
        var store = new ThemeStore(settings);

        store.SetAndSave(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, store.Current);
        Assert.Equal(ThemePreference.Dark, settings.Current.App.Theme);
    }

    [Fact]
    public void Current_SeesAChangeMadeDirectlyInTheSettingsStore()
    {
        SettingsStore settings = SettingsStore.InMemory(new UserSettings());
        var store = new ThemeStore(settings);

        settings.Update(s => s with { App = s.App with { Theme = ThemePreference.Light } });

        Assert.Equal(ThemePreference.Light, store.Current);
    }
}
