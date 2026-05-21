namespace WallpaperTurbo.Core.Services.Performance;

/// <summary>
/// Specifies the rule for when rendering playback should be suspended.
/// </summary>
public enum PauseMode
{
    /// <summary>
    /// Suspends playback only when a foreground window is maximized or running in fullscreen.
    /// </summary>
    Maximized,

    /// <summary>
    /// Suspends playback whenever any non-desktop application has keyboard focus.
    /// </summary>
    Focused,

    /// <summary>
    /// Completely disables performance-based pausing.
    /// </summary>
    None
}
