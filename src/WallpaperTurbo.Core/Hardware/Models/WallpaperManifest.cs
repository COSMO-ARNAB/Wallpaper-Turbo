// WallpaperManifest.cs - Defines the data model for the wallpaper manifest, which includes metadata about available wallpapers such as title,
namespace WallpaperTurbo.Core.Models;

public class WallpaperManifest
{
    public IReadOnlyList<WallpaperEntry> Wallpapers { get; init; } = [];
}

public class WallpaperEntry
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Video { get; init; } = string.Empty;

    public string Thumbnail { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string LayoutMode { get; init; } = "Fill";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Playlists { get; init; } = [];

    public WallpaperLayoutMode GetLayoutMode()
    {
        if (Enum.TryParse<WallpaperLayoutMode>(LayoutMode, true, out var mode))
        {
            return mode;
        }
        return WallpaperLayoutMode.Fill;
    }
}