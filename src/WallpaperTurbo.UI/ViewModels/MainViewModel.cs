using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WallpaperService _wallpaperService;
    private readonly TelemetryService _telemetryService;

    [ObservableProperty]
    private object? _currentPageViewModel;

    [ObservableProperty]
    private bool _isEngineRunning;

    [ObservableProperty]
    private string _engineStatusText = "ENGINE STOPPED";

    [ObservableProperty]
    private string _uptimeText = "00:00:00";

    [ObservableProperty]
    private string _fpsText = "0";

    [ObservableProperty]
    private string _gpuText = "0%";

    [ObservableProperty]
    private string _vramText = "0 GB";

    [ObservableProperty]
    private string _ramText = "0 GB";

    [ObservableProperty]
    private string _activeRendererText = "None";

    [ObservableProperty]
    private string _activeWallpaperTitle = "No Active Wallpaper";

    [ObservableProperty]
    private string _activeWallpaperSpecs = "3840 x 2160 • 60 FPS";

    // ViewModels injection
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    public MainViewModel(
        WallpaperService wallpaperService,
        TelemetryService telemetryService,
        DashboardViewModel dashboardViewModel,
        LibraryViewModel libraryViewModel,
        SettingsViewModel settingsViewModel)
    {
        _wallpaperService = wallpaperService;
        _telemetryService = telemetryService;
        _dashboardViewModel = dashboardViewModel;
        _libraryViewModel = libraryViewModel;
        _settingsViewModel = settingsViewModel;

        // Initialize active view to Dashboard
        _currentPageViewModel = _dashboardViewModel;

        // Hook up telemetry polling updates
        _telemetryService.MetricsUpdated += OnMetricsUpdated;
        _telemetryService.Start();

        // Check initial engine status
        UpdateEngineStatus();
    }

    private void OnMetricsUpdated(TelemetryMetrics m)
    {
        // Update global performance status footer strings using high-efficiency property checks
        bool running = _wallpaperService.IsEngineRunning();
        if (IsEngineRunning != running) IsEngineRunning = running;
        
        string statusText = running ? "ENGINE RUNNING" : "ENGINE STOPPED";
        if (EngineStatusText != statusText) EngineStatusText = statusText;

        string uptime = running 
            ? string.Format("{0:00}:{1:00}:{2:00}", (int)m.Uptime.TotalHours, m.Uptime.Minutes, m.Uptime.Seconds)
            : "00:00:00";
        if (UptimeText != uptime) UptimeText = uptime;

        string fpsVal = running ? $"{m.Fps}" : "0";
        if (FpsText != fpsVal) FpsText = fpsVal;

        string gpuVal = running ? $"{m.GpuUsage:0}%" : "0%";
        if (GpuText != gpuVal) GpuText = gpuVal;

        string vramVal = running ? $"{m.VramUsageGb:0.0} / {m.VramTotalGb:0} GB" : $"0.0 / {m.VramTotalGb:0} GB";
        if (VramText != vramVal) VramText = vramVal;

        string ramVal = running ? $"{m.RamUsageGb:0.0} GB" : "0.0 GB";
        if (RamText != ramVal) RamText = ramVal;

        string renderer = running ? m.Renderer : "None";
        if (ActiveRendererText != renderer) ActiveRendererText = renderer;

        // Push update notification downward to active child VMs
        _dashboardViewModel.UpdateTelemetry(m);
    }

    [RelayCommand]
    private void Navigate(string destination)
    {
        switch (destination.ToLowerInvariant())
        {
            case "dashboard":
                CurrentPageViewModel = _dashboardViewModel;
                break;
            case "library":
                CurrentPageViewModel = _libraryViewModel;
                break;
            case "settings":
                CurrentPageViewModel = _settingsViewModel;
                break;
            default:
                break;
        }
    }

    [RelayCommand]
    private async Task ToggleEngineAsync()
    {
        if (IsEngineRunning)
        {
            await _wallpaperService.StopPlaybackAsync();
            ActiveWallpaperTitle = "No Active Wallpaper";
        }
        else
        {
            // Start default first wallpaper
            var list = await _wallpaperService.GetWallpapersAsync();
            if (list.Any())
            {
                await _wallpaperService.LaunchWallpaperAsync(1);
                ActiveWallpaperTitle = list[0].Title;
                ActiveWallpaperSpecs = $"{list[0].Resolution} • {list[0].Fps}";
            }
        }
        UpdateEngineStatus();
    }

    public void UpdateEngineStatus()
    {
        IsEngineRunning = _wallpaperService.IsEngineRunning();
        EngineStatusText = IsEngineRunning ? "ENGINE RUNNING" : "ENGINE STOPPED";
    }

    public void SetActiveWallpaperInfo(string title, string specs)
    {
        ActiveWallpaperTitle = title;
        ActiveWallpaperSpecs = specs;
    }
}
