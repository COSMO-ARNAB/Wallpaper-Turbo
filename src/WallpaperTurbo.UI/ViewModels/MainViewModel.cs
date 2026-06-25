using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    private readonly WallpaperService _wallpaperService;
    private readonly TelemetryService _telemetryService;
    private readonly IWallpaperLibraryService _libraryService;
    private readonly ISettingsStore _settingsStore;
    private readonly UpdaterViewModel _updater;
    private readonly LayoutHostViewModel _layoutHostViewModel;

    // Import cancellation / progress support
    private CancellationTokenSource? _importCts;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private int _importProgressPercent;

    [ObservableProperty]
    private string _importProgressText = string.Empty;

    [ObservableProperty]
    private object? _currentPageViewModel;

    partial void OnCurrentPageViewModelChanged(object? value)
    {
        string typeName = value != null ? value.GetType().Name : "null";
        StartupDiagnostics.Log($"CurrentPageViewModel assigned: {typeName}");

        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsLibraryActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }

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

    [ObservableProperty]
    private bool _isWhatsNewVisible;

    [ObservableProperty]
    private string _whatsNewVersion = string.Empty;

    [ObservableProperty]
    private System.Collections.Generic.List<string> _whatsNewHighlights = new();

    [ObservableProperty]
    private ICommand? _whatsNewCloseCommand;

    public System.Collections.ObjectModel.ObservableCollection<MonitorTopologyItem> Monitors { get; } = new();

    [ObservableProperty]
    private MonitorTopologyItem? _selectedMonitor;

    public bool IsDashboardActive => CurrentPageViewModel is DashboardViewModel;
    public bool IsLibraryActive => CurrentPageViewModel is LibraryViewModel;
    public bool IsSettingsActive => CurrentPageViewModel is SettingsViewModel;

    // ViewModels injection
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    public UpdaterViewModel Updater => _updater;

    public LayoutHostViewModel LayoutHost => _layoutHostViewModel;

    public PresentationManager Presentation { get; }

    public MainViewModel(
        WallpaperService wallpaperService,
        TelemetryService telemetryService,
        IWallpaperLibraryService libraryService,
        ISettingsStore settingsStore,
        UpdaterViewModel updater,
        DashboardViewModel dashboardViewModel,
        LibraryViewModel libraryViewModel,
        SettingsViewModel settingsViewModel,
        LayoutHostViewModel layoutHostViewModel,
        PresentationManager presentation)
    {
        StartupDiagnostics.Log("MainViewModel constructor ENTRY");
        _wallpaperService = wallpaperService;
        _telemetryService = telemetryService;
        _libraryService = libraryService;
        _settingsStore = settingsStore;
        _updater = updater;
        _dashboardViewModel = dashboardViewModel;
        _libraryViewModel = libraryViewModel;
        _settingsViewModel = settingsViewModel;
        _layoutHostViewModel = layoutHostViewModel;
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));

        _libraryService.MetadataChanged += OnWallpaperMetadataChanged;

        _currentPageViewModel = _dashboardViewModel;
        StartupDiagnostics.Log("CurrentPageViewModel assigned: DashboardViewModel");

        WhatsNewCloseCommand = new RelayCommand(() => IsWhatsNewVisible = false);

        // Hook up telemetry polling updates
        _telemetryService.MetricsUpdated += OnMetricsUpdated;
        _telemetryService.Start();

        // Initialize active monitors dynamically from MonitorManager
        try
        {
            var realMonitors = MonitorManager.GetMonitors();
            for (int i = 0; i < realMonitors.Count; i++)
            {
                var m = realMonitors[i];
                Monitors.Add(new MonitorTopologyItem
                {
                    Number = i + 1,
                    Resolution = $"{m.Width} x {m.Height}",
                    Type = m.IsPrimary ? "Primary" : "Secondary"
                });
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Failed to get monitors in MainViewModel: {ex.Message}");
        }

        if (Monitors.Count == 0)
        {
            // Fallback default
            Monitors.Add(new MonitorTopologyItem { Number = 1, Resolution = "1920 x 1080", Type = "Primary" });
        }
        SelectedMonitor = Monitors[0];

        // Check initial engine status
        UpdateEngineStatus();

        // Defer checking for version update release notes modal to non-blocking application idle priority
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            CheckForVersionUpdate();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        StartupDiagnostics.LogWithMemory("MainViewModel constructor EXIT");
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
            Filter = "Video Wallpapers (*.mp4;*.webm;*.mkv;*.gif)|*.mp4;*.webm;*.mkv;*.gif|" +
                     "Static Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|" +
                     "All Supported Formats (*.mp4;*.webm;*.mkv;*.gif;*.jpg;*.jpeg;*.png)|*.mp4;*.webm;*.mkv;*.gif;*.jpg;*.jpeg;*.png",
            FilterIndex = 1,
            Multiselect = true
        };

        if (openFileDialog.ShowDialog() != true)
            return;

        var files = openFileDialog.FileNames;
        int successCount = 0;
        bool wasCanceled = false;

        // Cancel any previous in-flight import and create a fresh CTS
        _importCts?.Cancel();
        _importCts?.Dispose();
        _importCts = new CancellationTokenSource();
        var cts = _importCts;

        IsImporting = true;
        ImportProgressPercent = 0;
        ImportProgressText = $"Preparing to import {files.Length} wallpaper(s)...";
        DialogTitle = "Importing Wallpapers";
        DialogMessage = ImportProgressText;
        IsDialogCancelVisible = true;
        DialogCancelCommand = CancelImportCommand;
            DialogConfirmCommand = new RelayCommand(() => { }); // no-op during import; Confirm is wired after completion
        IsDialogVisible = true;

        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                string file = files[i];
                ImportProgressText = $"Importing {i + 1} of {files.Length}: {System.IO.Path.GetFileName(file)}";
                DialogMessage = ImportProgressText;

                var progress = new Progress<ImportProgress>(p =>
                {
                    ImportProgressPercent = p.Percent;
                    ImportProgressText = $"Importing {i + 1} of {files.Length}: {p.Message}";
                    DialogMessage = ImportProgressText;
                });

                try
                {
                    var imported = await _libraryService.ImportWallpaperAsync(
                        file,
                        async (completedWp) =>
                        {
                            // Background dispatcher STA thumbnail finished, refresh UI on Main thread
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                            {
                                await _dashboardViewModel.LoadLibraryAsync();
                                await _libraryViewModel.LoadLibraryAsync();
                            });
                        },
                        cts.Token,
                        progress);

                    // Register the newly imported wallpaper in the Recently Used list
                    _dashboardViewModel.RegisterPlayedWallpaper(imported);

                    successCount++;
                }
                catch (OperationCanceledException)
                {
                    wasCanceled = true;
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to import '{file}': {ex.Message}");
                }
            }

            // Refresh library with imported wallpapers
            await _dashboardViewModel.LoadLibraryAsync();
            await _libraryViewModel.LoadLibraryAsync();

            // Always show a completion dialog that stays until the user dismisses it
            DialogTitle = "Import Complete";
            IsDialogCancelVisible = false;
            DialogConfirmCommand = new RelayCommand(() => IsDialogVisible = false);

            if (wasCanceled)
            {
                DialogMessage = successCount > 0
                    ? $"Import was cancelled after successfully importing {successCount} of {files.Length} wallpaper(s)."
                    : "Import was cancelled. No wallpapers were imported.";
            }
            else if (successCount < files.Length)
            {
                DialogMessage = files.Length == 1
                    ? "The selected file could not be imported. It may be locked, in an unsupported format, or a duplicate."
                    : $"Successfully imported {successCount} of {files.Length} wallpapers.\n\nSome files could not be imported due to file locks or invalid formats.";
            }
            else
            {
                DialogMessage = files.Length == 1
                    ? "Wallpaper imported successfully!"
                    : $"All {successCount} wallpapers imported successfully!";
            }
        }
        finally
        {
            IsImporting = false;
            ImportProgressPercent = 0;
            ImportProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _importCts?.Cancel();
        ImportProgressText = "Cancelling import...";
    }

    public async Task ShutdownAsync()
    {
        // Cancel telemetry updates
        _telemetryService.Stop();

        // Await all background library tasks (saving manifests, finishing thumbnails) safely
        await _libraryService.ShutdownAsync();
    }

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from telemetry events to prevent reference leak
        _telemetryService.MetricsUpdated -= OnMetricsUpdated;
        _libraryService.MetadataChanged -= OnWallpaperMetadataChanged;
    }

    #endregion

    private void OnWallpaperMetadataChanged(object? sender, WallpaperEntry e)
    {
        System.Windows.Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            try
            {
                await _dashboardViewModel.LoadLibraryAsync();
                await _libraryViewModel.LoadLibraryAsync();

                // Update active title/specs if the edited wallpaper is currently playing
                string title = e.Title ?? string.Empty;
                if (ActiveWallpaperTitle == title || ActiveWallpaperTitle == e.Title)
                {
                    SetActiveWallpaperInfo(title, $"{e.Resolution} • {e.Fps}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Failed to refresh after metadata change: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void ShowFeatureComingSoon(string featureName)
    {
        DialogTitle = $"{featureName} Mode";
        DialogMessage = $"The {featureName} configuration module is currently under active development for the next update.\n\nStay tuned to the Stable release channel for the update rollout!";
        IsDialogCancelVisible = false;
        DialogConfirmCommand = new RelayCommand(() => IsDialogVisible = false);
        IsDialogVisible = true;
    }

    /// <summary>
    /// Shows a confirmation dialog before proceeding to install the downloaded update.
    /// Install will close Wallpaper Turbo and run the Inno Setup installer; user must confirm.
    /// </summary>
    [RelayCommand]
    private void RequestInstallUpdate()
    {
        if (_updater.State != UpdateState.ReadyToInstall) return;

        var version = _updater.AvailableVersionText;
        var channel = _updater.ChannelDisplay;

        DialogTitle = "Install Update";
        DialogMessage = $"Wallpaper Turbo {version} ({channel}) is ready to install.\n\n" +
                        "The app will close and the installer will launch automatically. " +
                        "Any active wallpaper playback will be stopped.\n\n" +
                        "Continue with installation?";
        IsDialogCancelVisible = true;
        DialogConfirmCommand = new RelayCommand(() =>
        {
            IsDialogVisible = false;
            if (_updater.InstallCommand.CanExecute(null))
            {
                _updater.InstallCommand.Execute(null);
            }
        });
        DialogCancelCommand = new RelayCommand(() => IsDialogVisible = false);
        IsDialogVisible = true;
    }

    private void CheckForVersionUpdate()
    {
        try
        {
            var settings = _settingsStore.Load();
            string currentVersion = _updater.CurrentVersion;
            string lastVersion = settings.LastRunVersion;

            if (lastVersion != currentVersion)
            {
                if (string.Equals(settings.Layout, "Minimal", StringComparison.OrdinalIgnoreCase))
                {
                    // Show the premium glassmorphic Minimal layout What's New modal
                    WhatsNewVersion = currentVersion;
                    WhatsNewHighlights = new System.Collections.Generic.List<string>
                    {
                        "Dynamic glassbackdrop options (Acrylic, Mica, None) and smooth opacity adjustment slider inside Settings.",
                        "Optimized wallpaper engine startup and hardware acceleration decoding stability.",
                        "Improved titlebar click hit-testing, resolving unclickable min/max/close buttons.",
                        "Added navigation Back button on Library view to easily return to your dashboard."
                    };
                    IsWhatsNewVisible = true;
                }
                else
                {
                    // Show the "What's New" dialog inside the standard reddish dialog for Techie layout
                    DialogTitle = $"What's New in v{currentVersion}";
                    DialogMessage = "• Integrated dynamic glass backdrop custom controls and settings.\n" +
                                    "• Added smooth animations for Play and Delete library actions.\n" +
                                    "• Implemented 'Start with Windows' auto-start configurations.\n" +
                                    "• Enhanced wallpaper metadata management and performance.";
                    IsDialogCancelVisible = false;
                    DialogConfirmCommand = new RelayCommand(() => IsDialogVisible = false);
                    IsDialogVisible = true;
                }

                // Persist the current version as LastRunVersion
                settings.LastRunVersion = currentVersion;
                _settingsStore.Save(settings);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Failed to check version update: {ex.Message}");
        }
    }
}
