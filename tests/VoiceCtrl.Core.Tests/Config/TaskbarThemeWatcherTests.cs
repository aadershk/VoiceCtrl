using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class TaskbarThemeWatcherTests
{
    [Fact]
    public void ForTesting_ReflectsGivenTaskbarTheme_Light()
    {
        TaskbarThemeWatcher watcher = TaskbarThemeWatcher.ForTesting(isTaskbarLightTheme: true);

        Assert.True(watcher.IsTaskbarLightTheme);
    }

    [Fact]
    public void ForTesting_ReflectsGivenTaskbarTheme_Dark()
    {
        TaskbarThemeWatcher watcher = TaskbarThemeWatcher.ForTesting(isTaskbarLightTheme: false);

        Assert.False(watcher.IsTaskbarLightTheme);
    }

    [Fact]
    public void ForTesting_NeverRaisesTaskbarThemeChanged()
    {
        TaskbarThemeWatcher watcher = TaskbarThemeWatcher.ForTesting(isTaskbarLightTheme: true);
        bool raised = false;
        watcher.TaskbarThemeChanged += () => raised = true;

        watcher.Dispose();

        Assert.False(raised);
    }
}
