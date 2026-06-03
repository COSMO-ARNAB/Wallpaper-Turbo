using System;

namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface IVersionComparer
{
    bool IsUpdateAvailable(Version currentVersion, Version targetVersion);
}
