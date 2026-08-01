using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;
using WallpaperTurbo.UI.Models;

namespace WallpaperTurbo.UI.Services;

public class PowerManagementService : IDisposable
{
    private readonly WallpaperService _wallpaperService;
    private readonly ISettingsStore _settingsStore;
    private bool _pausedByBatterySaver = false;
    private bool _disposed = false;

    public PowerManagementService(WallpaperService wallpaperService, ISettingsStore settingsStore)
    {
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        // Listen for Windows Power Status changes (AC power vs Battery power, unplugged/plugged in)
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _settingsStore.SettingsChanged += OnSettingsChanged;

        // Perform initial check on startup
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
            EvaluatePowerState(reason: "Windows PowerModeChanged (StatusChange)");
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        EvaluatePowerState(reason: "Settings changed");
    }

    public void EvaluatePowerState(string reason)
    {
        try
        {
            var settings = _settingsStore.Load();
            bool onBattery = IsOnBatteryPower();

            Debug.WriteLine($"[PowerManagementService] EvaluatePowerState ({reason}): OnBattery={onBattery}, BatterySaverEnabled={settings.BatterySaverEnabled}");

            if (settings.BatterySaverEnabled && onBattery)
            {
                // If on battery and Battery Saver setting is enabled, pause engine if running
                if (_wallpaperService.IsEngineRunning())
                {
                    Debug.WriteLine("[PowerManagementService] Pausing wallpaper playback because system is on battery power.");
                    _pausedByBatterySaver = true;
                    _ = _wallpaperService.StopPlaybackAsync();
                }
            }
            else if (_pausedByBatterySaver && (!settings.BatterySaverEnabled || !onBattery))
            {
                // Resume wallpaper playback if system was plugged back in or Battery Saver was disabled
                Debug.WriteLine("[PowerManagementService] Resuming wallpaper playback because system is plugged in or Battery Saver was turned off.");
                _pausedByBatterySaver = false;

                int activeIndex = _wallpaperService.ActiveWallpaperIndex >= 0 
                    ? _wallpaperService.ActiveWallpaperIndex 
                    : _wallpaperService.LastActiveWallpaperIndex;

                if (activeIndex >= 0 && !_wallpaperService.IsEngineRunning())
                {
                    _ = _wallpaperService.LaunchWallpaperAsync(activeIndex);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerManagementService] Error during EvaluatePowerState: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _settingsStore.SettingsChanged -= OnSettingsChanged;
            _disposed = true;
        }
    }
}
