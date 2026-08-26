using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Services.Performance;
using WallpaperTurbo.Core.Services.Watchdog;
using Xunit;

namespace WallpaperTurbo.Tests;

public class ForegroundWindowWatcherTests
{
    [Fact]
    public void InitializingWithPauseModeNone_KeepsPauseModeNone()
    {
        using var watcher = new ForegroundWindowWatcher(PauseMode.None);
        Assert.Equal(PauseMode.None, watcher.PauseMode);
    }

    [Fact]
    public void SettingPauseModeNone_WhenPaused_ImmediatelyFiresUnobscuredVisibilityChanged()
    {
        using var watcher = new ForegroundWindowWatcher(PauseMode.Maximized);

        // Simulate a paused state by setting private field _lastState = true
        var field = typeof(ForegroundWindowWatcher).GetField("_lastState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(watcher, true);

        var events = new List<bool>();
        watcher.VisibilityChanged += obscured => events.Add(obscured);

        // Switch PauseMode to None
        watcher.PauseMode = PauseMode.None;

        Assert.Equal(new[] { false }, events);
        Assert.False((bool)field.GetValue(watcher)!);
    }

    [Fact]
    public void SettingPauseModeNone_WhenAlreadyPlaying_DoesNotFireRedundantEvent()
    {
        using var watcher = new ForegroundWindowWatcher(PauseMode.Maximized);

        var field = typeof(ForegroundWindowWatcher).GetField("_lastState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(watcher, false);

        var events = new List<bool>();
        watcher.VisibilityChanged += obscured => events.Add(obscured);

        watcher.PauseMode = PauseMode.None;

        Assert.Empty(events);
    }
}
