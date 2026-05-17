// WallpaperManifest.cs - Defines the data model for the wallpaper manifest, which includes metadata about available wallpapers such as title,
namespace WallpaperTurbo.Core.Models;

public class WallpaperManifest
{
    public List<WallpaperEntry> Wallpapers { get; set; } = new();
}

public class WallpaperEntry
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Video { get; set; } = string.Empty;

    public string Thumbnail { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = new();
}