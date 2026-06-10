using System;
using WallpaperTurbo.UI.Models;

namespace WallpaperTurbo.UI.Services;

public class SettingsStoreLayoutPreferenceStore : ILayoutPreferenceStore
{
    private readonly ISettingsStore _settingsStore;

    public SettingsStoreLayoutPreferenceStore(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public LayoutMode GetSavedLayout()
    {
        var settings = _settingsStore.Load();
        if (Enum.TryParse<LayoutMode>(settings.Layout, out var mode))
        {
            return mode;
        }
        return LayoutMode.Minimal;
    }

    public void SaveLayout(LayoutMode layoutMode)
    {
        var settings = _settingsStore.Load();
        settings.Layout = layoutMode.ToString();
        _settingsStore.Save(settings);
    }
}
