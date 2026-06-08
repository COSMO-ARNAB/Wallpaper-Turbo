using WallpaperTurbo.UI.Models;

namespace WallpaperTurbo.UI.Services;

public class InMemoryLayoutPreferenceStore : ILayoutPreferenceStore
{
    private LayoutMode _current = LayoutMode.Minimal;

    public LayoutMode GetSavedLayout() => _current;
    public void SaveLayout(LayoutMode layoutMode) => _current = layoutMode;
}