using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class ThemeResolverTests
{
    [Fact]
    public void System_WithOsLight_ResolvesLight()
    {
        ThemeResolver resolver = ThemeResolver.ForTesting(isOsLightTheme: true);

        Assert.Equal(Theme.Light, resolver.Resolve(ThemePreference.System));
    }

    [Fact]
    public void System_WithOsDark_ResolvesDark()
    {
        ThemeResolver resolver = ThemeResolver.ForTesting(isOsLightTheme: false);

        Assert.Equal(Theme.Dark, resolver.Resolve(ThemePreference.System));
    }

    [Fact]
    public void ExplicitLight_IgnoresOsState()
    {
        ThemeResolver resolver = ThemeResolver.ForTesting(isOsLightTheme: false);

        Assert.Equal(Theme.Light, resolver.Resolve(ThemePreference.Light));
    }

    [Fact]
    public void ExplicitDark_IgnoresOsState()
    {
        ThemeResolver resolver = ThemeResolver.ForTesting(isOsLightTheme: true);

        Assert.Equal(Theme.Dark, resolver.Resolve(ThemePreference.Dark));
    }

    [Fact]
    public void ForTesting_NeverRaisesOsThemeChanged()
    {
        ThemeResolver resolver = ThemeResolver.ForTesting(isOsLightTheme: true);
        bool raised = false;
        resolver.OsThemeChanged += () => raised = true;

        resolver.Dispose();

        Assert.False(raised);
    }
}
