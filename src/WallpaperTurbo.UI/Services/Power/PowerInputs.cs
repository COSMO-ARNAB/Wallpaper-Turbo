namespace WallpaperTurbo.UI.Services.Power;

/// <summary>
/// Snapshot of everything the battery-saver decision depends on, captured once per
/// evaluation so the policy cannot observe a torn view of the world.
/// </summary>
/// <param name="OnBattery">True when the machine is running unplugged.</param>
/// <param name="BatterySaverEnabled">The user's BatterySaverEnabled setting.</param>
/// <param name="EngineRunning">True when the AppRunner engine process is alive.</param>
/// <param name="PausedByBatterySaver">
/// True when playback is currently paused *by this feature* (as opposed to stopped by the
/// user). This is the outcome state that makes the decision idempotent.
/// </param>
public readonly record struct PowerInputs(
    bool OnBattery,
    bool BatterySaverEnabled,
    bool EngineRunning,
    bool PausedByBatterySaver);
