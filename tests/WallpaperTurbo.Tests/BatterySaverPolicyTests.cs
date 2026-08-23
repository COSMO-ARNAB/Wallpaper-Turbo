using WallpaperTurbo.UI.Services.Power;
using Xunit;

namespace WallpaperTurbo.Tests;

public class BatterySaverPolicyTests
{
    private static PowerAction Decide(bool onBattery, bool saverEnabled, bool engineRunning, bool alreadyPaused)
        => new BatterySaverPolicy().Decide(new PowerInputs(
            OnBattery: onBattery,
            BatterySaverEnabled: saverEnabled,
            EngineRunning: engineRunning,
            PausedByBatterySaver: alreadyPaused));

    // Exhaustive truth table over all four inputs. The policy is pure, so this is the whole
    // specification.
    [Theory]
    // Plugged in, saver off
    [InlineData(false, false, false, false, PowerAction.None)]
    [InlineData(false, false, false, true, PowerAction.Resume)]
    [InlineData(false, false, true, false, PowerAction.None)]
    [InlineData(false, false, true, true, PowerAction.Resume)]
    // Plugged in, saver on -> saver is irrelevant while on AC
    [InlineData(false, true, false, false, PowerAction.None)]
    [InlineData(false, true, false, true, PowerAction.Resume)]
    [InlineData(false, true, true, false, PowerAction.None)]
    [InlineData(false, true, true, true, PowerAction.Resume)]
    // On battery, saver off -> feature disabled, but still undo our own pause
    [InlineData(true, false, false, false, PowerAction.None)]
    [InlineData(true, false, false, true, PowerAction.Resume)]
    [InlineData(true, false, true, false, PowerAction.None)]
    [InlineData(true, false, true, true, PowerAction.Resume)]
    // On battery, saver on -> the only branch that pauses
    [InlineData(true, true, false, false, PowerAction.None)]
    [InlineData(true, true, false, true, PowerAction.None)]
    [InlineData(true, true, true, false, PowerAction.Pause)]
    [InlineData(true, true, true, true, PowerAction.None)]
    public void Decide_MatchesTruthTable(
        bool onBattery, bool saverEnabled, bool engineRunning, bool alreadyPaused, PowerAction expected)
    {
        Assert.Equal(expected, Decide(onBattery, saverEnabled, engineRunning, alreadyPaused));
    }

    /// <summary>
    /// Regression for the input-equality gate: the startup evaluation runs before the engine
    /// launches, so it cannot pause. A gate that memoised "we already evaluated on-battery +
    /// saver-on" would short-circuit every later evaluation and the wallpaper would play on
    /// battery for the whole session. Re-deciding must still yield Pause once the engine is up,
    /// with identical power inputs.
    /// </summary>
    [Fact]
    public void Decide_StillPauses_WhenEngineStartsAfterAnUnactionableEvaluation()
    {
        // Startup: on battery, saver on, engine not up yet.
        Assert.Equal(PowerAction.None, Decide(onBattery: true, saverEnabled: true, engineRunning: false, alreadyPaused: false));

        // Engine launches. Nothing else changed.
        Assert.Equal(PowerAction.Pause, Decide(onBattery: true, saverEnabled: true, engineRunning: true, alreadyPaused: false));
    }

    [Fact]
    public void Decide_IsIdempotent_AfterPausing()
    {
        // Post-pause steady state: we own the pause and the engine is down.
        Assert.Equal(PowerAction.None, Decide(onBattery: true, saverEnabled: true, engineRunning: false, alreadyPaused: true));

        // Even if the engine were somehow still alive, do not issue a second stop.
        Assert.Equal(PowerAction.None, Decide(onBattery: true, saverEnabled: true, engineRunning: true, alreadyPaused: true));
    }

    [Fact]
    public void Decide_IsIdempotent_AfterResuming()
    {
        // Plugged back in -> Resume, which clears PausedByBatterySaver.
        Assert.Equal(PowerAction.Resume, Decide(onBattery: false, saverEnabled: true, engineRunning: false, alreadyPaused: true));

        // Next evaluation must not resume again.
        Assert.Equal(PowerAction.None, Decide(onBattery: false, saverEnabled: true, engineRunning: true, alreadyPaused: false));
    }

    [Fact]
    public void Decide_NeverResumes_AWallpaperTheUserStopped()
    {
        // Engine down on AC power, but not by our hand: leave it alone.
        Assert.Equal(PowerAction.None, Decide(onBattery: false, saverEnabled: false, engineRunning: false, alreadyPaused: false));
    }
}
