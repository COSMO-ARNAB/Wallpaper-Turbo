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
            // Resume the last wallpaper shown in the dashboard hero when available.
            var list = await _wallpaperService.GetWallpapersAsync();
            if (list.Any())
            {
                var preferredWallpaper = _dashboardViewModel.CurrentWallpaper;
                var wallpaperToLaunch = preferredWallpaper != null
                    ? list.FirstOrDefault(w => w.Id == preferredWallpaper.Id || w.Title == preferredWallpaper.Title)
                    : null;

                wallpaperToLaunch ??= list.First();

                int index = list.IndexOf(wallpaperToLaunch) + 1;
                if (index > 0)
                {
                    await _wallpaperService.LaunchWallpaperAsync(index);
                    ActiveWallpaperTitle = wallpaperToLaunch.Title;
                    ActiveWallpaperSpecs = $"{wallpaperToLaunch.Resolution} • {wallpaperToLaunch.Fps}";
                    _dashboardViewModel.LastDisplayedWallpaper = wallpaperToLaunch;
                }
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
            Title = "Import Cinematic Wallpapers",
            Filter = "Supported Formats (*.mp4;*.webm;*.mkv;*.gif;*.jpg;*.jpeg;*.png)|*.mp4;*.webm;*.mkv;*.gif;*.jpg;*.jpeg;*.png",
            Multiselect = true
        };

        if (openFileDialog.ShowDialog() == true)
        {
            var files = openFileDialog.FileNames;
            int successCount = 0;

            foreach (var file in files)
            {
                try
                {
                    // Trigger the non-blocking import pipeline for each file
                    await _libraryService.ImportWallpaperAsync(
                        file,
                        async (completedWp) =>
                        {
                            // Background dispatcher STA thumbnail finished, refresh UI on Main thread
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                            {
                                await _dashboardViewModel.LoadLibraryAsync();
                                await _libraryViewModel.LoadLibraryAsync();
                            });
                        }, CancellationToken.None);

                    successCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to import '{file}': {ex.Message}");
                }
            }

            // Instantly load placeholders for transient fluid UI
            await _dashboardViewModel.LoadLibraryAsync();
            await _libraryViewModel.LoadLibraryAsync();

            // If some or all imports failed, show a helpful status dialog
            if (successCount < files.Length)
            {
                DialogTitle = "Import Status";
                DialogMessage = $"Successfully imported {successCount} of {files.Length} wallpapers.\n\nSome files could not be imported due to file locks or invalid formats.";
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
