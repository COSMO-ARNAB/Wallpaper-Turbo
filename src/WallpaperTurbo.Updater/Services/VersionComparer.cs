using System;
using WallpaperTurbo.Core.Updates.Interfaces;

namespace WallpaperTurbo.Updater.Services;

public sealed class VersionComparer : IVersionComparer
{
    public bool IsUpdateAvailable(Version currentVersion, Version targetVersion)
    {
        if (currentVersion == null) throw new ArgumentNullException(nameof(currentVersion));
        if (targetVersion == null) throw new ArgumentNullException(nameof(targetVersion));

        return targetVersion > currentVersion;
    }
}