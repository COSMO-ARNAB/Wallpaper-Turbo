using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI.Models;
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
    private bool _isEngineStartupTimedOut;

    [ObservableProperty]
    private string _engineStartupMessage = string.Empty;

    [ObservableProperty]
    private bool _isPlaying = true;

    [ObservableProperty]
    private bool _isApplyingWallpaper;

    private const int MaxRecoveryAttempts = 3;

    /// <summary>
    /// How long a transition waits for the watchdog to confirm the wallpaper window is on screen
    /// before falling back to the "still starting / Retry" state.
    /// </summary>
    internal static readonly TimeSpan WallpaperVisibilityTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Gap between consecutive auto-restart attempts after a wallpaper loss.</summary>
    internal TimeSpan RecoveryRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    internal int RecoveryAttempts { get; private set; }

    private bool _isRecovering;

    /// <summary>
    /// Runs an engine transition operation with the loading state active for its
    /// full lifetime. The flag is set synchronously before the first await so the
    /// UI gives feedback immediately, and cleared in a finally so it never sticks.
    /// The operation returns whether a launch actually happened; when it did, the
    /// transition completes only once the wallpaper window is confirmed on screen
    /// (watchdog), capped at <see cref="WallpaperVisibilityTimeout"/> before falling back
    /// to the "still starting / Retry" state.
    /// </summary>
    public async Task<bool> RunWallpaperTransitionAsync(Func<Task<bool>> operation)
    {
        var timeout = WallpaperVisibilityTimeout;
        var sw = Stopwatch.StartNew();
        StartupDiagnostics.LogWithMemory($"RunWallpaperTransitionAsync START (timeout={timeout.TotalSeconds}s) [Stopwatch]");
        IsApplyingWallpaper = true;
        try
        {
            bool launched = await operation();
            if (!launched)
            {
                StartupDiagnostics.Log($"RunWallpaperTransitionAsync no launch after {sw.ElapsedMilliseconds}ms [Stopwatch]");
                return false;
            }

            _wallpaperVisibility.SetEngineExpected(true);
            var waitSw = Stopwatch.StartNew();
            StartupDiagnostics.Log($"WaitForVisible START (timeout={timeout.TotalSeconds}s) [Stopwatch]");
            bool visible = await _wallpaperVisibility.WaitForVisibleAsync(timeout);
            StartupDiagnostics.Log($"WaitForVisible END in {waitSw.ElapsedMilliseconds}ms: visible={visible} (budget={timeout.TotalSeconds}s) [Stopwatch]");
            if (!visible)
            {
                IsEngineStartupTimedOut = true;
                EngineStartupMessage = "Wallpaper engine is still starting. Please wait or click Retry.";
            }
            else
            {
                IsEngineStartupTimedOut = false;
                EngineStartupMessage = string.Empty;
            }

            StartupDiagnostics.LogWithMemory($"RunWallpaperTransitionAsync END in {sw.ElapsedMilliseconds}ms: visible={visible} [Stopwatch]");
            return visible;
        }
        finally
        {
            IsApplyingWallpaper = false;
        }
    }

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
    [NotifyPropertyChangedFor(nameof(IsDialogApplyNotVisible))]
    private bool _isDialogApplyVisible;

    public bool IsDialogApplyNotVisible => !IsDialogApplyVisible;

    [ObservableProperty]
    private ICommand? _dialogApplyCommand;

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

    // Services
    private readonly IWallpaperVisibilityMonitor _wallpaperVisibility;

    public UpdaterViewModel Updater => _updater;

    public LayoutHostViewModel LayoutHost => _layoutHostViewModel;

    public PresentationManager Presentation { get; }
    public TelemetryViewModel Telemetry { get; } = new();

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
        PresentationManager presentation,
        IWallpaperVisibilityMonitor wallpaperVisibility)
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
        _wallpaperVisibility = wallpaperVisibility ?? throw new ArgumentNullException(nameof(wallpaperVisibility));

        _libraryService.MetadataChanged += OnWallpaperMetadataChanged;

        // Watchdog: watch for the wallpaper actually disappearing from the desktop
        // and auto-recover it. Runs as long as the app lives.
        _wallpaperVisibility.VisibilityChanged += OnWallpaperVisibilityChanged;
        _wallpaperVisibility.WallpaperLost += OnWallpaperLost;
        _wallpaperVisibility.Start();

        // Intentional engine restarts (GPU-preference switch) must pause the watchdog so it
        // does not mistake the temporary window disappearance for a crash and relaunch mid-restart.
        _wallpaperService.EngineRestarting += OnEngineRestarting;
        _wallpaperService.EngineRestartCompleted += OnEngineRestartCompleted;

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
        Telemetry.Update(m, running);
        if (IsEngineRunning != running) IsEngineRunning = running;
        
        string statusText = running ? "ENGINE RUNNING" : "ENGINE STOPPED";
        if (EngineStatusText != statusText) EngineStatusText = statusText;

        string uptime = running 
            ? string.Format("{0:00}:{1:00}:{2:00}", (int)m.Uptime.TotalHours, m.Uptime.Minutes, m.Uptime.Seconds)
            : "00:00:00";
        if (UptimeText != uptime) UptimeText = uptime;

        string fpsVal = running && m.IsFpsAvailable ? $"{m.Fps}" : "N/A";
        if (FpsText != fpsVal) FpsText = fpsVal;

        string gpuVal = running && m.IsGpuAvailable ? $"{m.GpuUsage:0.0}%" : "N/A";
        if (GpuText != gpuVal) GpuText = gpuVal;

        string vramVal = running && m.IsVramAvailable ? $"{m.VramUsageGb:0.00} GB" : "N/A";
        if (VramText != vramVal) VramText = vramVal;

        string ramVal = running && m.IsRamAvailable ? $"{m.RamUsageGb:0.00} GB" : "N/A";
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
            // Intentional stop — the watchdog must not auto-restart.
            _wallpaperVisibility.SetEngineExpected(false);
            await _wallpaperService.StopPlaybackAsync();
            ActiveWallpaperTitle = "No Active Wallpaper";
        }
        else
        {
            // Start engine with the last genuinely active wallpaper.
            // LastActiveWallpaperId is the one source of truth for restore (written only
            // when AppRunner confirms playback) — never the recent-history fallback.
            await RunWallpaperTransitionAsync(async () =>
            {
                var wallpaperToLaunch = await ResolveWallpaperToLaunchAsync();
                if (wallpaperToLaunch == null)
                {
                    return false;
                }

                var list = await _wallpaperService.GetWallpapersAsync();
                int index = list.IndexOf(wallpaperToLaunch) + 1;
                if (index <= 0)
                {
                    return false;
                }

                await _wallpaperService.LaunchWallpaperAsync(index);
                SetActiveWallpaperInfo(wallpaperToLaunch.Title, $"{wallpaperToLaunch.Resolution} • {wallpaperToLaunch.Fps}");
                _dashboardViewModel.LastDisplayedWallpaper = wallpaperToLaunch;
                return true;
            });
        }
        UpdateEngineStatus();
    }

    /// <summary>
    /// Resolves which wallpaper a start/recovery should launch: the engine-confirmed
    /// LastActiveWallpaperId first, falling back to the dashboard's current wallpaper,
    /// then the first library entry. Null when the library is empty.
    /// </summary>
    private async Task<WallpaperEntry?> ResolveWallpaperToLaunchAsync()
    {
        var list = await _wallpaperService.GetWallpapersAsync();
        if (!list.Any())
        {
            return null;
        }

        var lastActiveId = _settingsStore.Load().LastActiveWallpaperId;
        var wallpaperToLaunch = !string.IsNullOrEmpty(lastActiveId)
            ? list.FirstOrDefault(w => w.Id == lastActiveId)
            : null;

        // Fall back to the dashboard's current wallpaper only if no persisted ID resolves
        if (wallpaperToLaunch == null)
        {
            var preferredWallpaper = _dashboardViewModel.CurrentWallpaper;
            wallpaperToLaunch = preferredWallpaper != null
                ? list.FirstOrDefault(w => w.Id == preferredWallpaper.Id || w.Title == preferredWallpaper.Title)
                : null;
        }

        return wallpaperToLaunch ?? list[0];
    }

    // ── Watchdog recovery ────────────────────────────────────────────────────

    private void OnWallpaperVisibilityChanged(object? sender, bool visible)
    {
        // A visible wallpaper means the engine recovered — reset the failure counter.
        if (visible)
        {
            RecoveryAttempts = 0;
        }
    }

    private void OnEngineRestarting(object? sender, EventArgs e)
    {
        // An intentional restart is about to drop the wallpaper window; disarm the watchdog
        // so it doesn't fire WallpaperLost and collide with the restart.
        _wallpaperVisibility.SetEngineExpected(false);
    }

    private void OnEngineRestartCompleted(object? sender, EventArgs e)
    {
        // Re-arm only if the engine is actually running again (the restart succeeded).
        if (_wallpaperService.IsEngineRunning())
        {
            _wallpaperVisibility.SetEngineExpected(true);
        }
    }

    private async void OnWallpaperLost(object? sender, EventArgs e)
    {
        try
        {
            await TryRecoverWallpaperAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainViewModel] Wallpaper recovery failed: {ex.Message}");
            // A recovery failure must never spiral into a relaunch loop. Disarm the
            // watchdog and surface a non-fatal retry state so the user can recover manually.
            try
            {
                _wallpaperVisibility.SetEngineExpected(false);
            }
            catch
            {
                // Ignoring — we are already in a failure path.
            }

            IsEngineStartupTimedOut = true;
            EngineStartupMessage = "The wallpaper engine lost its display window and recovery failed. Click Retry to try again.";
            UpdateEngineStatus();
        }
    }

    /// <summary>
    /// Auto-restart policy: relaunch the last confirmed wallpaper, waiting (with the
    /// loading state active) until the watchdog confirms it is back on screen. Up to
    /// <see cref="MaxRecoveryAttempts"/> attempts spaced <see cref="RecoveryRetryDelay"/>
    /// apart; after that, surface the retry banner and mark the engine stopped.
    /// </summary>
    private async Task TryRecoverWallpaperAsync()
    {
        // Also bail if the watchdog has been disarmed (engine intentionally stopped/off).
        // This guards against a WallpaperLost event that was already queued on the
        // dispatcher before a stop completed, which would otherwise relaunch the engine
        // the user just turned off.
        if (_isRecovering || IsApplyingWallpaper || !IsEngineRunning || !_wallpaperVisibility.IsEngineExpected)
        {
            return;
        }

        _isRecovering = true;
        try
        {
            while (RecoveryAttempts < MaxRecoveryAttempts && IsEngineRunning)
            {
                RecoveryAttempts++;
                bool recovered = await RunWallpaperTransitionAsync(async () =>
                {
                    var wallpaperToLaunch = await ResolveWallpaperToLaunchAsync();
                    if (wallpaperToLaunch == null)
                    {
                        return false;
                    }

                    var list = await _wallpaperService.GetWallpapersAsync();
                    int index = list.IndexOf(wallpaperToLaunch) + 1;
                    if (index <= 0)
                    {
                        return false;
                    }

                    await _wallpaperService.LaunchWallpaperAsync(index);
                    SetActiveWallpaperInfo(wallpaperToLaunch.Title, $"{wallpaperToLaunch.Resolution} • {wallpaperToLaunch.Fps}");
                    return true;
                });

                if (recovered)
                {
                    return;
                }

                if (RecoveryAttempts >= MaxRecoveryAttempts)
                {
                    break;
                }

                await Task.Delay(RecoveryRetryDelay);
            }

            if (!IsEngineRunning)
            {
                return;
            }

            _wallpaperVisibility.SetEngineExpected(false);
            ActiveWallpaperTitle = "No Active Wallpaper";
            ActiveWallpaperSpecs = "None";
            IsEngineStartupTimedOut = true;
            EngineStartupMessage = "The wallpaper engine lost its display window and could not be recovered automatically. Click Retry to try again.";
            UpdateEngineStatus();
        }
        finally
        {
            _isRecovering = false;
        }
    }

    /// <summary>
    /// Applies the outcome of the startup coordinator so the UI can surface
    /// "engine is still starting" with a retry path instead of hanging silently.
    /// </summary>
    public void ApplyStartupResult(StartupResult result)
    {
        // Arm the watchdog's auto-recovery for the startup wallpaper, but ONLY when
        // a wallpaper is genuinely running. If the engine isn't actually up (timeout
        // or no active wallpaper) we explicitly disarm it so the watchdog never tries
        // to "recover" a wallpaper that was never started — which would spawn a
        // spurious AppRunner process. Stop/pause/delete paths later call
        // SetEngineExpected(false) themselves, and OnWallpaperLost is additionally
        // gated on IsEngineRunning, so a stuck-true state cannot trigger recovery.
        bool engineExpected = !result.TimedOut && result.IsEngineRunning && result.ActiveWallpaper != null;
        _wallpaperVisibility.SetEngineExpected(engineExpected);

        if (result.TimedOut)
        {
            IsEngineStartupTimedOut = true;
            EngineStartupMessage = result.ErrorMessage ?? "Wallpaper engine is still starting.";
        }
        else
        {
            IsEngineStartupTimedOut = false;
            EngineStartupMessage = string.Empty;

            if (result.IsEngineRunning && result.ActiveWallpaper != null)
            {
                SetActiveWallpaperInfo(
                    result.ActiveWallpaper.Title,
                    $"{result.ActiveWallpaper.Resolution} • {result.ActiveWallpaper.Fps}");
                _dashboardViewModel.LastDisplayedWallpaper = result.ActiveWallpaper;
            }
        }
        UpdateEngineStatus();
    }

    [RelayCommand]
    private async Task RetryEngineStartAsync()
    {
        IsEngineStartupTimedOut = false;
        EngineStartupMessage = string.Empty;

        var coordinator = App.GetService<WallpaperStartupCoordinator>();
        if (coordinator == null)
        {
            return;
        }

        var result = await coordinator.EnsureWallpaperRunningAsync();
        ApplyStartupResult(result);
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
            // Intentional stop — the watchdog must not auto-restart.
            _wallpaperVisibility.SetEngineExpected(false);
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
        WallpaperEntry? importedWallpaper = null;

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
                            // Refresh the UI after thumbnail generation completes.
                            await RefreshLibrariesOnUiThreadAsync();
                        },
                        cts.Token,
                        progress);

                    // Register the newly imported wallpaper in the Recently Used list
                    _dashboardViewModel.RegisterPlayedWallpaper(imported);

                    importedWallpaper = imported;
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

            if (files.Length == 1 && successCount == 1 && importedWallpaper != null)
            {
                IsDialogApplyVisible = true;
                DialogConfirmCommand = new RelayCommand(() => IsDialogVisible = false);
                DialogApplyCommand = new AsyncRelayCommand(async () =>
                {
                    IsDialogVisible = false;
                    await PlayWallpaperAsync(importedWallpaper);
                });
            }
            else
            {
                IsDialogApplyVisible = false;
                DialogConfirmCommand = new RelayCommand(() => IsDialogVisible = false);
            }

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
        _wallpaperVisibility.VisibilityChanged -= OnWallpaperVisibilityChanged;
        _wallpaperVisibility.WallpaperLost -= OnWallpaperLost;
        _wallpaperService.EngineRestarting -= OnEngineRestarting;
        _wallpaperService.EngineRestartCompleted -= OnEngineRestartCompleted;
        _wallpaperVisibility.Stop();
    }

    #endregion

    private void OnWallpaperMetadataChanged(object? sender, WallpaperEntry e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _ = RefreshLibrariesAfterMetadataChangeAsync(e);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => _ = RefreshLibrariesAfterMetadataChangeAsync(e)));
    }

    private async Task RefreshLibrariesOnUiThreadAsync()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            await _dashboardViewModel.LoadLibraryAsync();
            await _libraryViewModel.LoadLibraryAsync();
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            _ = _dashboardViewModel.LoadLibraryAsync();
            _ = _libraryViewModel.LoadLibraryAsync();
        });
    }

    private async Task RefreshLibrariesAfterMetadataChangeAsync(WallpaperEntry e)
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

    [RelayCommand]
    private void OpenFolder(WallpaperEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Video) || !System.IO.File.Exists(entry.Video))
            return;

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{entry.Video}\"");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Failed to open folder: {ex.Message}");
        }
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

    private static readonly System.Collections.Generic.List<string> CurrentVersionHighlights = new()
    {
        "v1.5.1 Auto-Updater verification release",
        "Dynamically resolved installer log paths to eliminate UAC elevation errors",
        "Enhanced UTF-8 BOM safety across updater network providers",
        "Includes full battery saver auto-pause and auto-resume capabilities"
    };

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
                    WhatsNewHighlights = CurrentVersionHighlights;
                    IsWhatsNewVisible = true;
                }
                else
                {
                    // Show the "What's New" dialog inside the standard reddish dialog for Techie layout
                    DialogTitle = $"What's New in v{currentVersion}";
                    DialogMessage = string.Join("\n", CurrentVersionHighlights.ConvertAll(h => $"• {h}"));
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

    private async Task PlayWallpaperAsync(WallpaperEntry wp)
    {
        try
        {
            var list = await _wallpaperService.GetWallpapersAsync();
            int index = list.FindIndex(x => x.Id == wp.Id) + 1;
            if (index > 0)
            {
                var settings = _settingsStore.Load();
                bool pause = settings.PauseOnMaximized;

                // Stop any current playback first for clean transition
                if (IsEngineRunning)
                {
                    await _wallpaperService.StopPlaybackAsync();
                    await Task.Delay(500);
                }

                await _wallpaperService.LaunchWallpaperAsync(index, pause ? "Maximized" : "None", forceFreshLaunch: true);

                // Refresh library selections in view models
                await _dashboardViewModel.LoadLibraryAsync();
                await _libraryViewModel.LoadLibraryAsync();

                UpdateEngineStatus();
                SetActiveWallpaperInfo(wp.Title, $"{wp.Resolution} • {wp.Fps}");
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Failed to apply imported wallpaper: {ex.Message}");
        }
    }
}
