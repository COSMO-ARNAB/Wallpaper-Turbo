using System;
using System.Reflection;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Tests;

public sealed class InnoSetupApplierTests
{
    [Fact]
    public void Constructor_WhenInstallArgsIsNull_UsesInnoSetupManagedLogPath()
    {
        var applier = new InnoSetupApplier(null);

        var installArgs = typeof(InnoSetupApplier)
            .GetField("_installArgs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(applier);

        Assert.Equal("/LOG", installArgs);
    }
}
