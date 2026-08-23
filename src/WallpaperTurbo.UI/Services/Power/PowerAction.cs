namespace WallpaperTurbo.UI.Services.Power;

/// <summary>
/// The single side effect a battery-saver evaluation may request.
/// </summary>
public enum PowerAction
{
    /// <summary>Nothing to do — the current state already matches the desired state.</summary>
    None = 0,

    /// <summary>Stop playback and remember that battery saver owns the pause.</summary>
    Pause = 1,

    /// <summary>Release the battery-saver pause and relaunch if a wallpaper is known.</summary>
    Resume = 2,
}
