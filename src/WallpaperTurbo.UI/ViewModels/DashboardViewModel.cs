using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All";

    // Real-Time Telemetry Properties
    [ObservableProperty] private double _gpuValue = 18;
    [ObservableProperty] private double _videoDecodeValue = 11;
    [ObservableProperty] private double _cpuValue = 6;
    [ObservableProperty] private string _ramValueText = "5.1 / 32 GB";
    [ObservableProperty] private string _vramValueText = "1.2 / 8 GB";

    [ObservableProperty] private double _ramPercentage = 16;
    [ObservableProperty] private double _vramPercentage = 15;

    // Engine Status Properties
    [ObservableProperty] private string _rendererText = "VLC (D3D11VA)";
    [ObservableProperty] private string _hardwareDecodeText = "Enabled";
    [ObservableProperty] private string _dwmCompositionText = "Optimized";
    [ObservableProperty] private string _workerWText = "Yes";
    [ObservableProperty] private string _presentationText = "Below Icons";
    [ObservableProperty] private string _frameSyncText = "Optimized";

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

    public DashboardViewModel(WallpaperService wallpaperService, DiagnosticsService diagnosticsService)
    {
        _wallpaperService = wallpaperService;
        Diagnostics = diagnosticsService;

        // Add mock monitor layouts matching reference image (3840x2160 Primary, 2560x1440 Secondary)
        Monitors.Add(new MonitorTopologyItem { Number = 1, Resolution = "3840 x 2160", Type = "Primary" });
        Monitors.Add(new MonitorTopologyItem { Number = 2, Resolution = "2560 x 1440", Type = "Secondary" });

        // Load dynamic library
        _ = LoadLibraryAsync();
    }

    public async Task LoadLibraryAsync()
    {
        _allWallpapers = await _wallpaperService.GetWallpapersAsync();
        await LoadFeaturedWallpapersAsync();
        ApplyFilter();
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
            RamPercentage = (m.RamUsageGb / m.RamTotalGb) * 100.0;
            _lastRam = m.RamUsageGb;
        }

        // Vram formatting with 0.05 GB filter
        if (Math.Abs(m.VramUsageGb - _lastVram) >= 0.05 || m.VramUsageGb == 0.0)
        {
            VramValueText = $"{m.VramUsageGb:0.0} / {m.VramTotalGb:0} GB";
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
        var list = await _wallpaperService.GetWallpapersAsync();
        int index = list.IndexOf(wp) + 1;
        if (index > 0)
        {
            await _wallpaperService.LaunchWallpaperAsync(index, PauseOnMaximized ? "Maximized" : "None");
            
            // Notify MainViewModel of active wallpaper details
            App.GetService<MainViewModel>().SetActiveWallpaperInfo(wp.Title, $"{wp.Resolution} • {wp.Fps}");
        }
    }
}
