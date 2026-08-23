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

    private bool _pausedByBatterySaver;
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
                PausedByBatterySaver: _pausedByBatterySaver);

            var action = _policy.Decide(inputs);

            Debug.WriteLine(
                $"[PowerManagementService] EvaluatePowerState ({reason}): OnBattery={onBattery}, " +
                $"BatterySaverEnabled={settings.BatterySaverEnabled}, EngineRunning={engineRunning}, " +
                $"PausedByBatterySaver={_pausedByBatterySaver} -> {action}");

            switch (action)
            {
                case PowerAction.Pause:
                    Debug.WriteLine("[PowerManagementService] Pausing wallpaper playback because system is on battery power.");
                    _pausedByBatterySaver = true;
                    _ = _wallpaperService.StopPlaybackAsync();
                    break;

                case PowerAction.Resume:
                    Debug.WriteLine("[PowerManagementService] Resuming wallpaper playback because system is plugged in or Battery Saver was turned off.");
                    _pausedByBatterySaver = false;

                    int activeIndex = _wallpaperService.ActiveWallpaperIndex >= 0
                        ? _wallpaperService.ActiveWallpaperIndex
                        : _wallpaperService.LastActiveWallpaperIndex;

                    if (activeIndex >= 0 && !engineRunning)
                    {
                        _ = _wallpaperService.LaunchWallpaperAsync(activeIndex);
                    }
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
