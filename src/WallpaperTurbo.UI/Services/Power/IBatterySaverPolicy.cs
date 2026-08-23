namespace WallpaperTurbo.UI.Services.Power;

/// <summary>
/// Pure decision function for battery-saver playback control. Kept free of Windows APIs,
/// timers and services so the full truth table is unit-testable.
/// </summary>
public interface IBatterySaverPolicy
{
    PowerAction Decide(PowerInputs inputs);
}
