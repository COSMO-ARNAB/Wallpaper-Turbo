using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface IUpdaterSettingsStore
{
    UpdaterSettings Load();
    void Save(UpdaterSettings settings);
}
