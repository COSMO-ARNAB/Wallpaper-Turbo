namespace WallpaperTurbo.Core.Models;

/// <summary>
/// Specifies how the wallpaper video should be scaled and fitted to the screen aspect ratio.
/// </summary>
public enum WallpaperLayoutMode
{
    /// <summary>
    /// Crops left/right or top/bottom to fill the screen while maintaining native aspect ratio.
    /// </summary>
    Fill,

    /// <summary>
    /// Scales the video to fit within the screen bounds with black borders preserving native aspect ratio.
    /// </summary>
    Fit,

    /// <summary>
    /// Stretches the video directly to match screen dimensions, ignoring native aspect ratio.
    /// </summary>
    Stretch
}
