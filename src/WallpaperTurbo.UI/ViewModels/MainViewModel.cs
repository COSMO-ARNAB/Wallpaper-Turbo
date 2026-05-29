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
    private readonly IWallpaperLibraryService _libraryService;

    [ObservableProperty]
    private object? _currentPageViewModel;

    [ObservableProperty]
    private bool _isEngineRunning;

    [ObservableProperty]
    private bool _isPlaying = true;

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

    [ObservableProperty]
    private bool _isDialogVisible;

    [ObservableProperty]
    private string _dialogTitle = string.Empty;

    [ObservableProperty]
    private string _dialogMessage = string.Empty;

    [ObservableProperty]
    private bool _isDialogCancelVisible;

    [ObservableProperty]
    private ICommand? _dialogConfirmCommand;

    [ObservableProperty]
    private ICommand? _dialogCancelCommand;

    // ViewModels injection
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    public MainViewModel(
        WallpaperService wallpaperService,
        TelemetryService telemetryService,
        IWallpaperLibraryService libraryService,
        DashboardViewModel dashboardViewModel,
        LibraryViewModel libraryViewModel,
        SettingsViewModel settingsViewModel)
    {
        _wallpaperService = wallpaperService;
        _telemetryService = telemetryService;
        _libraryService = libraryService;
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

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (IsEngineRunning)
        {
            bool success = await _wallpaperService.ResumePlaybackAsync();
            if (success) IsPlaying = true;
        }
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (IsEngineRunning)
        {
            bool success = await _wallpaperService.PausePlaybackAsync();
            if (success) IsPlaying = false;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (IsEngineRunning)
        {
            await _wallpaperService.StopPlaybackAsync();
            ActiveWallpaperTitle = "No Active Wallpaper";
            ActiveWallpaperSpecs = "None";
            IsPlaying = true;
            UpdateEngineStatus();
        }
    }

    public void UpdateEngineStatus()
    {
        IsEngineRunning = _wallpaperService.IsEngineRunning();
        EngineStatusText = IsEngineRunning ? "ENGINE RUNNING" : "ENGINE STOPPED";
        if (!IsEngineRunning)
        {
            IsPlaying = true;
        }
    }

    public void SetActiveWallpaperInfo(string title, string specs)
    {
        ActiveWallpaperTitle = title;
        ActiveWallpaperSpecs = specs;
    }

    [RelayCommand]
    private async Task ImportWallpaperAsync()
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Cinematic Wallpaper",
            Filter = "Supported Formats (*.mp4;*.webm;*.mkv;*.gif;*.jpg;*.jpeg;*.png)|*.mp4;*.webm;*.mkv;*.gif;*.jpg;*.jpeg;*.png",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                // Trigger the non-blocking import pipeline
                var newWp = await _libraryService.ImportWallpaperAsync(
                    openFileDialog.FileName,
                    async (completedWp) =>
                    {
                        // Background dispatcher STA thumbnail finished, refresh UI on Main thread
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await _dashboardViewModel.LoadLibraryAsync();
                            await _libraryViewModel.LoadLibraryAsync();
                        });
                    }, CancellationToken.None);

                // Instantly load placeholders for transient fluid UI
                await _dashboardViewModel.LoadLibraryAsync();
                await _libraryViewModel.LoadLibraryAsync();
            }
            catch (Exception ex)
            {
                DialogTitle = "Import Failure";
                DialogMessage = $"Failed to import wallpaper:\n{ex.Message}";
                IsDialogCancelVisible = false;
                DialogConfirmCommand = new RelayCommand(() => IsDialogVisible = false);
                IsDialogVisible = true;
            }
        }
    }

    public async Task ShutdownAsync()
    {
        // Cancel telemetry updates
        _telemetryService.Stop();

        // Await all background library tasks (saving manifests, finishing thumbnails) safely
        await _libraryService.ShutdownAsync();
    }
}
