using System;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Tests;

public sealed class InnoSetupApplierTests
{
    [Fact]
    public void Constructor_WhenInstallArgsIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InnoSetupApplier(null!));
    }
}
