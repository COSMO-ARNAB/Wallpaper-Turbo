namespace WallpaperTurbo.UI.Services.Power;

/// <summary>
/// Pure decision function for battery-saver playback control. Kept free of Windows APIs,
/// timers and services so the full truth table is unit-testable.
/// </summary>
public interface IBatterySaverPolicy
{
    /// <summary>
    /// Decides what to do about playback that is already running (or already suppressed).
    /// </summary>
    PowerAction Decide(PowerInputs inputs);

    /// <summary>
    /// True when battery-saver conditions mean playback must not be running right now.
    /// Callers about to *start* playback ask this instead of <see cref="Decide"/>, because
    /// <see cref="Decide"/> can only ever pause something that is already running.
    /// </summary>
    /// <remarks>
    /// This exists so the startup path can decline to launch rather than launching and then
    /// immediately stopping — which cost a process spawn, GPU init and a decoded first frame
    /// on battery, and showed the user a visible flash of wallpaper. Sharing one predicate
    /// keeps that gate from drifting away from the pause/resume decision.
    /// </remarks>
    bool SuppressesPlayback(PowerInputs inputs);
}
