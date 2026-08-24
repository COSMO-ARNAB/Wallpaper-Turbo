using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services.Power;

namespace WallpaperTurbo.UI.Services;

public class PowerManagementService : IDisposable
{
    /// <summary>
    /// Window used to coalesce bursts of power/settings/session notifications into a single
    /// evaluation. Internal so tests can reuse the exact production value.
    /// </summary>
    internal static readonly TimeSpan EvaluationDebounceWindow = TimeSpan.FromMilliseconds(500);

    private readonly WallpaperService _wallpaperService;
    private readonly ISettingsStore _settingsStore;
    private readonly IBatterySaverPolicy _policy;
    private readonly Debouncer _debouncer;

    private bool _suppressedByBatterySaver;
    private bool _disposed;

    /// <summary>0 = idle, 1 = an evaluation is in flight. int so it can be used with Interlocked.</summary>
    private int _evalInProgress;

    public PowerManagementService(WallpaperService wallpaperService, ISettingsStore settingsStore)
        : this(wallpaperService, settingsStore, new BatterySaverPolicy(), TimeProvider.System)
    {
    }

    public PowerManagementService(
        WallpaperService wallpaperService,
        ISettingsStore settingsStore,
        IBatterySaverPolicy policy,
        TimeProvider timeProvider)
    {
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        ArgumentNullException.ThrowIfNull(timeProvider);

        _debouncer = new Debouncer(
            EvaluationDebounceWindow,
            () => EvaluatePowerState(reason: "Debounced power/settings/session notification"),
            timeProvider);

        // Listen for Windows Power Status changes (AC power vs Battery power, unplugged/plugged in)
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _settingsStore.SettingsChanged += OnSettingsChanged;

        // The startup evaluation below runs before the engine exists, so it cannot pause anything.
        // Re-evaluate once a session actually becomes active, otherwise booting on battery with
        // battery saver enabled would play all session long until some unrelated power event fired.
        _wallpaperService.SessionStateChanged += OnSessionStateChanged;

        // Immediate, not debounced: nothing has arrived yet to coalesce with.
        EvaluatePowerState(reason: "Startup initialization");
    }

    /// <summary>
    /// Evaluates whether system is currently running on battery power (unplugged).
    /// </summary>
    public static bool IsOnBatteryPower()
    {
        try
        {
            var status = SystemInformation.PowerStatus;
            return status.PowerLineStatus == PowerLineStatus.Offline;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerManagementService] Failed to check power line status: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Evaluates whether system is currently plugged into AC power.
    /// </summary>
    public static bool IsPluggedIn()
    {
        return !IsOnBatteryPower();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
        {
            _debouncer.Schedule();
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _debouncer.Schedule();
    }

    private void OnSessionStateChanged(object? sender, WallpaperSessionEventArgs e)
    {
        // Only the transition *into* an active session can create new work (something to pause).
        // Ignoring deactivation also keeps us off IsEngineRunning()'s own engine-died publish,
        // which EvaluatePowerState triggers — so this cannot feed back on itself.
        if (!e.IsActive)
        {
            return;
        }

        _debouncer.Schedule();
    }

    public void EvaluatePowerState(string reason)
    {
        if (Interlocked.CompareExchange(ref _evalInProgress, 1, 0) != 0)
        {
            Debug.WriteLine($"[PowerManagementService] EvaluatePowerState re-entrancy guard ({reason}): skipped");
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            bool onBattery = IsOnBatteryPower();

            // Probed once and reused: IsEngineRunning() has side effects (state-file sync,
            // active-index reset, session publish), so calling it twice per evaluation would
            // publish twice.
            bool engineRunning = _wallpaperService.IsEngineRunning();

            var inputs = new PowerInputs(
                OnBattery: onBattery,
                BatterySaverEnabled: settings.BatterySaverEnabled,
                EngineRunning: engineRunning,
                SuppressedByBatterySaver: _suppressedByBatterySaver);

            var action = _policy.Decide(inputs);

            Debug.WriteLine(
                $"[PowerManagementService] EvaluatePowerState ({reason}): OnBattery={onBattery}, " +
                $"BatterySaverEnabled={settings.BatterySaverEnabled}, EngineRunning={engineRunning}, " +
                $"SuppressedByBatterySaver={_suppressedByBatterySaver} -> {action}");

            switch (action)
            {
                case PowerAction.Pause:
                    Debug.WriteLine("[PowerManagementService] Freezing wallpaper playback because system is on battery power.");
                    _suppressedByBatterySaver = true;
                    _ = FreezePlaybackAsync();
                    break;

                case PowerAction.Resume:
                    Debug.WriteLine("[PowerManagementService] Resuming wallpaper playback because system is plugged in or Battery Saver was turned off.");
                    _suppressedByBatterySaver = false;

                    int activeIndex = _wallpaperService.ActiveWallpaperIndex >= 0
                        ? _wallpaperService.ActiveWallpaperIndex
                        : _wallpaperService.LastActiveWallpaperIndex;

                    _ = ThawPlaybackAsync(activeIndex, engineRunning);
                    break;

                case PowerAction.None:
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerManagementService] Error during EvaluatePowerState: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _evalInProgress, 0);
        }
    }

    /// <summary>
    /// Stops the engine decoding without tearing it down, leaving the last frame on the desktop.
    /// </summary>
    /// <remarks>
    /// This used to call <see cref="WallpaperService.StopPlaybackAsync"/>, which terminates
    /// AppRunner outright. Unplugging therefore blanked the desktop — indistinguishable from the
    /// wallpaper having crashed — threw away the process, its GPU context and its decoder setup,
    /// and made plugging back in cost a full cold launch. A frozen engine spends no CPU or GPU
    /// decode time, so it still meets the point of battery saver.
    ///
    /// The stop survives as a fallback only: if the engine will not answer IPC we cannot know it
    /// has stopped decoding, and an unresponsive process draining the battery is worse than a
    /// blank desktop.
    /// </remarks>
    private async Task FreezePlaybackAsync()
    {
        if (await _wallpaperService.PausePlaybackAsync())
        {
            return;
        }

        Debug.WriteLine("[PowerManagementService] Engine did not answer the IPC pause; stopping playback instead.");
        await _wallpaperService.StopPlaybackAsync();
    }

    /// <summary>
    /// Undoes <see cref="FreezePlaybackAsync"/>, launching instead when there is nothing frozen.
    /// </summary>
    /// <remarks>
    /// Both paths are reachable. Normally the engine is still alive and only needs unfreezing, which
    /// is instant. But a startup that declined to launch on battery, or a freeze that had to fall
    /// back to a stop, leaves no process at all — there the recorded index is the only way back.
    /// </remarks>
    private async Task ThawPlaybackAsync(int activeIndex, bool engineRunning)
    {
        if (engineRunning && await _wallpaperService.ResumePlaybackAsync())
        {
            return;
        }

        if (activeIndex >= 0)
        {
            await _wallpaperService.LaunchWallpaperAsync(activeIndex);
        }
    }

    /// <summary>
    /// Records that startup deliberately did not launch the engine because of battery saver.
    /// </summary>
    /// <remarks>
    /// Without this, declining to launch would be indistinguishable from the user having stopped
    /// playback themselves: <see cref="PowerInputs.SuppressedByBatterySaver"/> would stay false,
    /// the policy would return <see cref="PowerAction.None"/> on plug-in, and battery saver would
    /// behave as a silent off-switch for the rest of the session instead of a deferral.
    /// <see cref="WallpaperStartupCoordinator"/> also records the index it would have launched, so
    /// the resume below has a target even though nothing ever ran.
    /// </remarks>
    public void NotifyPlaybackSuppressedAtStartup()
    {
        Debug.WriteLine("[PowerManagementService] Startup declined to launch the engine (battery saver); will resume when plugged in.");
        _suppressedByBatterySaver = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _settingsStore.SettingsChanged -= OnSettingsChanged;
        _wallpaperService.SessionStateChanged -= OnSessionStateChanged;
        _debouncer.Dispose();
    }
}
