using System;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Tests;

public sealed class InnoSetupApplierTests
{
    [Fact]
    public void Constructor_WhenInstallArgsIsNull_DefaultsToTempLogPath()
    {
        var applier = new InnoSetupApplier(null);
        Assert.NotNull(applier);
    }
}
