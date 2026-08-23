namespace WallpaperTurbo.UI.Services.Power;

/// <summary>
/// Decides whether playback should be paused or resumed for battery saver.
/// </summary>
/// <remarks>
/// Idempotence comes from <b>outcome</b> state (<see cref="PowerInputs.PausedByBatterySaver"/>,
/// <see cref="PowerInputs.EngineRunning"/>) rather than from remembering the previous
/// <i>inputs</i>. That distinction matters: an input-equality gate treats "we already looked at
/// this state" as "we already acted on this state", which silently disables the feature whenever
/// the first evaluation could not act (e.g. it ran before the engine had launched). Re-deciding
/// from scratch every time is cheap and self-healing.
/// </remarks>
public sealed class BatterySaverPolicy : IBatterySaverPolicy
{
    public PowerAction Decide(PowerInputs inputs)
    {
        if (inputs.BatterySaverEnabled && inputs.OnBattery)
        {
            // Pause only when there is something to pause and we have not already done it.
            return !inputs.PausedByBatterySaver && inputs.EngineRunning
                ? PowerAction.Pause
                : PowerAction.None;
        }

        // Plugged in, or the setting is off: undo our own pause, but never touch a stop
        // the user asked for.
        return inputs.PausedByBatterySaver ? PowerAction.Resume : PowerAction.None;
    }
}
