using System;
using WallpaperTurbo.UI.Models;

namespace WallpaperTurbo.UI.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
    event EventHandler<AppSettings>? SettingsChanged;
}
