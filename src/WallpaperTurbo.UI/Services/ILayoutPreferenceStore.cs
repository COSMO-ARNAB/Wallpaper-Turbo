using WallpaperTurbo.UI.Models;

namespace WallpaperTurbo.UI.Services;

public interface ILayoutPreferenceStore
{
    LayoutMode GetSavedLayout();
    void SaveLayout(LayoutMode layoutMode);
}