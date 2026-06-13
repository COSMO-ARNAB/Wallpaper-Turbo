using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public class MonitorTopologyItem
{
    public int Number { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DisplayString => $"{Resolution}\n{Type}";
}

public partial class DashboardViewModel : ObservableObject
{
    private readonly WallpaperService _wallpaperService;
    private List<WallpaperEntry> _allWallpapers = new();

    // Collection of dynamic wallpapers bound to the Library grid
    public ObservableCollection<WallpaperEntry> FilteredWallpapers { get; } = new();

    // Collection of recently used wallpapers
    public ObservableCollection<WallpaperEntry> RecentlyUsedWallpapers { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All";

    // Dynamic Greeting Banner Properties
    [ObservableProperty] private string _greetingHeader = "Good Evening";
    [ObservableProperty] private string _greetingSubtext = "Your engine is running smoothly.";

    // Real-Time Telemetry Properties
    [ObservableProperty] private double _gpuValue = 18;
    [ObservableProperty] private double _videoDecodeValue = 11;
    [ObservableProperty] private double _cpuValue = 6;
    [ObservableProperty] private string _ramValueText = "5.1 / 32 GB";
    [ObservableProperty] private string _vramValueText = "1.2 / 8 GB";
    [ObservableProperty] private string _ramGbText = "3.1 GB";
    [ObservableProperty] private string _vramGbText = "1.2 GB";

    [ObservableProperty] private double _ramPercentage = 16;
    [ObservableProperty] private double _vramPercentage = 15;

    // Engine Status Properties
    [ObservableProperty] private string _rendererText = "VLC (D3D11VA)";
    [ObservableProperty] private string _hardwareDecodeText = "Enabled";
    [ObservableProperty] private string _dwmCompositionText = "Optimized";
    [ObservableProperty] private string _workerWText = "Yes";
    [ObservableProperty] private string _presentationText = "Below Icons";
    [ObservableProperty] private string _frameSyncText = "Optimized";

    private readonly ISettingsStore _settingsStore;
    private bool _isSyncing = false;

    // Quick Controls properties (switches)
    [ObservableProperty] private bool _pauseOnMaximized = true;
    [ObservableProperty] private bool _muteAudio = false;
    [ObservableProperty] private bool _startWithWindows = true;
    [ObservableProperty] private bool _autoStartEngine = true;

    // Featured Hero Section Wallpapers
    [ObservableProperty] private WallpaperEntry? _heroWallpaper;
    [ObservableProperty] private WallpaperEntry? _subHero1;
    [ObservableProperty] private WallpaperEntry? _subHero2;
    [ObservableProperty] private WallpaperEntry? _subHero3;
    [ObservableProperty] private WallpaperEntry? _activeWallpaper;
    [ObservableProperty] private WallpaperEntry? _lastDisplayedWallpaper;

    public WallpaperEntry? CurrentWallpaper => ActiveWallpaper ?? LastDisplayedWallpaper ?? HeroWallpaper;

    public bool IsCurrentWallpaperPlaying => ActiveWallpaper != null && CurrentWallpaper != null && ActiveWallpaper.Id == CurrentWallpaper.Id;

    public bool HasWallpapers => _allWallpapers.Count > 0;

    [ObservableProperty]
    private bool _isLoading = true;

    partial void OnActiveWallpaperChanged(WallpaperEntry? value)
    {
        OnPropertyChanged(nameof(CurrentWallpaper));
        OnPropertyChanged(nameof(IsCurrentWallpaperPlaying));
        if (value != null)
        {
            LastDisplayedWallpaper = value;
            RegisterPlayedWallpaper(value);
        }
    }

    partial void OnLastDisplayedWallpaperChanged(WallpaperEntry? value)
    {
        OnPropertyChanged(nameof(CurrentWallpaper));
        OnPropertyChanged(nameof(IsCurrentWallpaperPlaying));
    }

    partial void OnHeroWallpaperChanged(WallpaperEntry? value)
    {
        OnPropertyChanged(nameof(CurrentWallpaper));
        OnPropertyChanged(nameof(IsCurrentWallpaperPlaying));
    }

    // Collection of active monitors
    public ObservableCollection<MonitorTopologyItem> Monitors { get; } = new();

    private double _lastGpu = -1;
    private double _lastVideoDecode = -1;
    private double _lastCpu = -1;
    private double _lastRam = -1;
    private double _lastVram = -1;

    public DiagnosticsService Diagnostics { get; }

    public System.Windows.Visibility DevOverlayVisibility
    {
        get
        {
#if DEBUG
            return System.Windows.Visibility.Visible;
#else
            return System.Windows.Visibility.Collapsed;
#endif
        }
    }

    public DashboardViewModel(
        WallpaperService wallpaperService, 
        DiagnosticsService diagnosticsService,
        ISettingsStore settingsStore)
    {
        StartupDiagnostics.Log("DashboardViewModel constructor ENTRY");
        _wallpaperService = wallpaperService;
        Diagnostics = diagnosticsService;
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        // Hydrate from settings store
        var settings = _settingsStore.Load();
        _isSyncing = true;
        try
        {
            _muteAudio = settings.MuteAudio;
            _pauseOnMaximized = settings.PauseOnMaximized;
        }
        finally
        {
            _isSyncing = false;
        }

        // Add active monitors dynamically from MonitorManager
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
            StartupDiagnostics.Log($"Failed to get monitors in DashboardViewModel: {ex.Message}");
        }

        if (Monitors.Count == 0)
        {
            // Fallback default
            Monitors.Add(new MonitorTopologyItem { Number = 1, Resolution = "1920 x 1080", Type = "Primary" });
        }

        UpdateGreeting();

        // Listen to external settings changes
        _settingsStore.SettingsChanged += OnSettingsStoreChanged;

        // Load dynamic library
        _ = LoadLibraryAsync();
        StartupDiagnostics.LogWithMemory("DashboardViewModel constructor EXIT");
    }

    private void OnSettingsStoreChanged(object? sender, AppSettings newSettings)
    {
        App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            _isSyncing = true;
            try
            {
                if (MuteAudio != newSettings.MuteAudio) MuteAudio = newSettings.MuteAudio;
                if (PauseOnMaximized != newSettings.PauseOnMaximized) PauseOnMaximized = newSettings.PauseOnMaximized;
            }
            finally
            {
                _isSyncing = false;
            }
        }));
    }

    partial void OnMuteAudioChanged(bool value)
    {
        if (_isSyncing) return;

        _ = _wallpaperService.SetMuteAsync(value);

        var settings = _settingsStore.Load();
        settings.MuteAudio = value;
        _settingsStore.Save(settings);
    }

    partial void OnPauseOnMaximizedChanged(bool value)
    {
        if (_isSyncing) return;

        _wallpaperService.ActivePauseProfile = value ? "Maximized" : "Disabled";

        var settings = _settingsStore.Load();
        settings.PauseOnMaximized = value;
        _settingsStore.Save(settings);
    }

    public void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        if (hour >= 5 && hour < 12)
        {
            GreetingHeader = "Good Morning";
        }
        else if (hour >= 12 && hour < 17)
        {
            GreetingHeader = "Good Afternoon";
        }
        else
        {
            GreetingHeader = "Good Evening";
        }

        if (_wallpaperService != null)
        {
            GreetingSubtext = _wallpaperService.IsEngineRunning()
                ? "Your engine is running smoothly."
                : "Start the engine to activate live desktop.";
        }
    }

    public async Task LoadLibraryAsync()
    {
        try
        {
            _allWallpapers = await _wallpaperService.GetWallpapersAsync();
            await LoadFeaturedWallpapersAsync();
            await LoadRecentlyUsedHistoryAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] LoadLibraryAsync error: {ex}");
        }
        finally
        {
            OnPropertyChanged(nameof(HasWallpapers));
            IsLoading = false;
        }

        // Auto-start engine on startup if configured and offline
        if (AutoStartEngine && !_wallpaperService.IsEngineRunning())
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); // Give the WPF UI a 1-second window to render fluidly
                var mainVm = App.GetService<MainViewModel>();
                var app = System.Windows.Application.Current;
                if (app?.Dispatcher != null && mainVm != null)
                {
                    await app.Dispatcher.InvokeAsync(async () =>
                    {
                        if (!mainVm.IsEngineRunning)
                        {
                            await mainVm.ToggleEngineCommand.ExecuteAsync(null);
                        }
                    });
                }
            });
        }
    }

    public async Task LoadFeaturedWallpapersAsync()
    {
        if (_allWallpapers.Count >= 4)
        {
            HeroWallpaper = _allWallpapers[0]; // Astral Horizon / Crimson Blind
            SubHero1 = _allWallpapers[1];      // Retrowave Drive / Red Leaves
            SubHero2 = _allWallpapers[2];      // Forest Serenity / Rapi Red
            SubHero3 = _allWallpapers[3];      // Sukuna Madness
        }
        else if (_allWallpapers.Count >= 3)
        {
            HeroWallpaper = _allWallpapers[0];
            SubHero1 = _allWallpapers[1];
            SubHero2 = _allWallpapers[2];
            SubHero3 = _allWallpapers[0];
        }
        else if (_allWallpapers.Count > 0)
        {
            HeroWallpaper = _allWallpapers[0];
            SubHero1 = _allWallpapers[0];
            SubHero2 = _allWallpapers[0];
            SubHero3 = _allWallpapers[0];
        }
        await Task.CompletedTask;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
    }

    public void ApplyFilter()
    {
        var query = _allWallpapers.AsEnumerable();

        // 1. Search text filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string searchLower = SearchText.ToLowerInvariant();
            query = query.Where(w => w.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                                     w.Author.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                                     w.Tags.Any(t => t.Contains(searchLower, StringComparison.OrdinalIgnoreCase)));
        }

        // 2. Selected Category filter
        if (!string.Equals(SelectedCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            string categoryLower = SelectedCategory.ToLowerInvariant();
            if (categoryLower == "4k")
            {
                query = query.Where(w => w.Resolution.Contains("3840"));
            }
            else if (categoryLower == "ultrawide")
            {
                query = query.Where(w => w.Resolution.Contains("3440"));
            }
            else
            {
                query = query.Where(w => w.Tags.Any(t => t.Equals(categoryLower, StringComparison.OrdinalIgnoreCase)));
            }
        }

        var targetList = query.ToList();

        // 3. Incremental synchronization to avoid list-level resetting
        // Remove items no longer present
        for (int i = FilteredWallpapers.Count - 1; i >= 0; i--)
        {
            if (!targetList.Contains(FilteredWallpapers[i]))
            {
                FilteredWallpapers.RemoveAt(i);
            }
        }

        // Add or move items to match targetList exactly in order
        for (int i = 0; i < targetList.Count; i++)
        {
            var targetItem = targetList[i];
            int currentIndex = FilteredWallpapers.IndexOf(targetItem);
            if (currentIndex == -1)
            {
                FilteredWallpapers.Insert(i, targetItem);
            }
            else if (currentIndex != i)
            {
                FilteredWallpapers.Move(currentIndex, i);
            }
        }
    }

    public void UpdateTelemetry(TelemetryMetrics m)
    {
        UpdateGreeting();

        // Sync ActiveWallpaper property based on running state and active title
        var mainVm = App.GetService<MainViewModel>();
        if (mainVm != null)
        {
            if (!mainVm.IsEngineRunning)
            {
                if (ActiveWallpaper != null) ActiveWallpaper = null;
            }
            else
            {
                string activeTitle = mainVm.ActiveWallpaperTitle;
                if (ActiveWallpaper == null || ActiveWallpaper.Title != activeTitle)
                {
                    ActiveWallpaper = _allWallpapers.FirstOrDefault(w => w.Title == activeTitle);
                }
            }
        }

        // 0.5% dead-band filter to prevent layout over-refresh stutters for micro-changes
        if (Math.Abs(m.GpuUsage - _lastGpu) >= 0.5 || m.GpuUsage == 0.0)
        {
            GpuValue = m.GpuUsage;
            _lastGpu = m.GpuUsage;
        }

        if (Math.Abs(m.VideoDecodeUsage - _lastVideoDecode) >= 0.5 || m.VideoDecodeUsage == 0.0)
        {
            VideoDecodeValue = m.VideoDecodeUsage;
            _lastVideoDecode = m.VideoDecodeUsage;
        }

        if (Math.Abs(m.CpuUsage - _lastCpu) >= 0.5 || m.CpuUsage == 0.0)
        {
            CpuValue = m.CpuUsage;
            _lastCpu = m.CpuUsage;
        }
        
        // Ram formatting with 0.1 GB filter
        if (Math.Abs(m.RamUsageGb - _lastRam) >= 0.1 || m.RamUsageGb == 0.0)
        {
            RamValueText = $"{m.RamUsageGb:0.0} / {m.RamTotalGb:0} GB";
            RamGbText = $"{m.RamUsageGb:0.0} GB";
            RamPercentage = (m.RamUsageGb / m.RamTotalGb) * 100.0;
            _lastRam = m.RamUsageGb;
        }

        // Vram formatting with 0.05 GB filter
        if (Math.Abs(m.VramUsageGb - _lastVram) >= 0.05 || m.VramUsageGb == 0.0)
        {
            VramValueText = $"{m.VramUsageGb:0.0} / {m.VramTotalGb:0} GB";
            VramGbText = $"{m.VramUsageGb:0.0} GB";
            VramPercentage = (m.VramUsageGb / m.VramTotalGb) * 100.0;
            _lastVram = m.VramUsageGb;
        }

        // Engine Status Indicators (Only trigger property setters if values actually changed)
        if (RendererText != m.Renderer) RendererText = m.Renderer;
        if (HardwareDecodeText != m.HardwareDecodeStatus) HardwareDecodeText = m.HardwareDecodeStatus;
        
        string compositionState = m.IsDwmCompositionEnabled ? "Optimized" : "Disabled";
        if (DwmCompositionText != compositionState) DwmCompositionText = compositionState;

        string workerWState = m.IsWorkerWAttached ? "Yes" : "No";
        if (WorkerWText != workerWState) WorkerWText = workerWState;
    }

    [RelayCommand]
    private async Task PlayWallpaperAsync(WallpaperEntry? wp)
    {
        if (wp == null) return;

        // If the wallpaper is already active and playing, toggle it off by stopping playback
        if (ActiveWallpaper != null && wp.Id == ActiveWallpaper.Id)
        {
            var mainVm = App.GetService<MainViewModel>();
            if (mainVm != null)
            {
                await mainVm.StopCommand.ExecuteAsync(null);
            }
            return;
        }

        var list = await _wallpaperService.GetWallpapersAsync();
        int index = list.IndexOf(wp) + 1;
        if (index > 0)
        {
            RegisterPlayedWallpaper(wp);
            LastDisplayedWallpaper = wp;
            await _wallpaperService.LaunchWallpaperAsync(index, PauseOnMaximized ? "Maximized" : "None");
            ActiveWallpaper = wp;

            // Notify MainViewModel of active wallpaper details
            App.GetService<MainViewModel>().SetActiveWallpaperInfo(wp.Title, $"{wp.Resolution} • {wp.Fps}");
        }
    }

    [RelayCommand]
    private async Task TripleClickPlayWallpaperAsync(WallpaperEntry? wp)
    {
        if (wp == null) return;
        var list = await _wallpaperService.GetWallpapersAsync();
        int index = list.IndexOf(wp) + 1;
        if (index > 0)
        {
            RegisterPlayedWallpaper(wp);
            LastDisplayedWallpaper = wp;
            
            // 1. Force close any previous wallpaper completely first
            var mainVm = App.GetService<MainViewModel>();
            if (mainVm.IsEngineRunning)
            {
                await _wallpaperService.StopPlaybackAsync();
                await Task.Delay(500); // Give it a short delay for OS process cleanup
            }

            // 2. Play the new wallpaper fresh from scratch (forcing fresh process)
            await _wallpaperService.LaunchWallpaperAsync(index, PauseOnMaximized ? "Maximized" : "None", forceFreshLaunch: true);
            ActiveWallpaper = wp;

            // 3. Update main status details
            mainVm.UpdateEngineStatus();
            mainVm.SetActiveWallpaperInfo(wp.Title, $"{wp.Resolution} • {wp.Fps}");
        }
    }

    // ── Persistent Recently Used History Engine ────────────────────────────────
    
    private const string HistoryFileName = "recent_history.json";
    
    private string GetHistoryPath()
    {
        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperTurbo");
        return Path.Combine(appDataDir, HistoryFileName);
    }

    private async Task LoadRecentlyUsedHistoryAsync()
    {
        try
        {
            string path = GetHistoryPath();
            bool hasHistory = File.Exists(path);
            List<string> ids = new();
            if (hasHistory)
            {
                string json = await File.ReadAllTextAsync(path);
                ids = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            }

            var list = new List<WallpaperEntry>();
            foreach (var id in ids)
            {
                var wp = _allWallpapers.FirstOrDefault(w => w.Id == id);
                if (wp != null)
                {
                    list.Add(wp);
                }
            }

            // Fallback to first 5 wallpapers if history is clean/new
            if (list.Count == 0 && _allWallpapers.Count > 0)
            {
                list = _allWallpapers.Take(5).ToList();
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                RecentlyUsedWallpapers.Clear();
                foreach (var wp in list)
                {
                    RecentlyUsedWallpapers.Add(wp);
                }

                if (hasHistory && list.Count > 0)
                {
                    LastDisplayedWallpaper = list[0];
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Load history error: {ex.Message}");
            // Reliable fallback
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                RecentlyUsedWallpapers.Clear();
                foreach (var wp in _allWallpapers.Take(5))
                {
                    RecentlyUsedWallpapers.Add(wp);
                }
            });
        }
    }

    private async Task SaveRecentlyUsedHistoryAsync()
    {
        try
        {
            string path = GetHistoryPath();
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            List<string> ids;
            lock (RecentlyUsedWallpapers)
            {
                ids = RecentlyUsedWallpapers.Select(w => w.Id).ToList();
            }

            string json = JsonSerializer.Serialize(ids);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Save history error: {ex.Message}");
        }
    }

    private void RegisterPlayedWallpaper(WallpaperEntry wp)
    {
        if (wp == null) return;
        
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            var existing = RecentlyUsedWallpapers.FirstOrDefault(w => w.Id == wp.Id);
            if (existing != null)
            {
                RecentlyUsedWallpapers.Remove(existing);
            }
            
            RecentlyUsedWallpapers.Insert(0, wp);
            
            // Limit to 8 items max
            while (RecentlyUsedWallpapers.Count > 8)
            {
                RecentlyUsedWallpapers.RemoveAt(RecentlyUsedWallpapers.Count - 1);
            }
        });

        _ = SaveRecentlyUsedHistoryAsync();
    }
}

