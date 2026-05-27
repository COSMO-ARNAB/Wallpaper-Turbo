using System.Threading.Tasks;

namespace WallpaperTurbo.UI.Services;

/// <summary>
/// Defines a contract for managing globally exclusive, async-safe, UI-thread-affiliated hover previews of wallpapers.
/// </summary>
public interface IWallpaperPreviewService
{
    /// <summary>
    /// Initiates a debounced, async-safe hover preview session on the specified wallpaper entry.
    /// </summary>
    /// <param name="entry">The wallpaper entry to preview.</param>
    Task StartPreviewAsync(WallpaperEntry entry);

    /// <summary>
    /// Instantly cancels, stops, and disposes of the active preview session.
    /// </summary>
    Task StopPreviewAsync();

    /// <summary>
    /// Resets the inactivity timer during active user hover interaction inside a card.
    /// </summary>
    void ResetTimer();
}
