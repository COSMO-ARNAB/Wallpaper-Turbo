using System;
using WallpaperTurbo.UI.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

/// <summary>
/// Guards the readiness budget against log/behaviour drift. The timeout the coordinator reports
/// used to be hard-coded ("5s") independently of the loop that actually waited 100 × 100ms, so the
/// two silently disagreed by a factor of two. Deriving the timeout from the poll interval and
/// attempt count makes that impossible; this test pins the derivation.
/// </summary>
public class WallpaperStartupCoordinatorReadinessTests
{
    [Fact]
    public void ReadinessTimeout_IsDerivedFromThePollLoop()
    {
        var expected = TimeSpan.FromMilliseconds(
            (long)WallpaperStartupCoordinator.ReadinessPollIntervalMs * WallpaperStartupCoordinator.ReadinessMaxAttempts);

        Assert.Equal(expected, WallpaperStartupCoordinator.ReadinessTimeout);
    }

    [Fact]
    public void ReadinessTimeout_IsTenSeconds()
    {
        Assert.Equal(10, WallpaperStartupCoordinator.ReadinessTimeout.TotalSeconds);
    }
}
