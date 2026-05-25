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

    // Collection of active monitors
    public ObservableCollection<MonitorTopologyItem> Monitors { get; } = new();

    public DashboardViewModel(WallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService;

        // Add mock monitor layouts matching reference image (3840x2160 Primary, 2560x1440 Secondary)
        Monitors.Add(new MonitorTopologyItem { Number = 1, Resolution = "3840 x 2160", Type = "Primary" });
        Monitors.Add(new MonitorTopologyItem { Number = 2, Resolution = "2560 x 1440", Type = "Secondary" });

        // Load Hero wallpapers asynchronous
        _ = LoadFeaturedWallpapersAsync();
    }

    public async Task LoadFeaturedWallpapersAsync()
    {
        var list = await _wallpaperService.GetWallpapersAsync();
        if (list.Count >= 3)
        {
            HeroWallpaper = list[0]; // Astral Horizon / Crimson Blind
            SubHero1 = list[1];      // Retrowave Drive / Red Leaves
            SubHero2 = list[2];      // Forest Serenity / Rapi Red
        }
        else if (list.Count > 0)
        {
            HeroWallpaper = list[0];
            SubHero1 = list[0];
            SubHero2 = list[0];
        }
    }

    public void UpdateTelemetry(TelemetryMetrics m)
    {
        GpuValue = m.GpuUsage;
        VideoDecodeValue = m.VideoDecodeUsage;
        CpuValue = m.CpuUsage;
        
        // Ram formatting
        RamValueText = $"{m.RamUsageGb:0.0} / {m.RamTotalGb:0} GB";
        RamPercentage = (m.RamUsageGb / m.RamTotalGb) * 100.0;

        // Vram formatting
        VramValueText = $"{m.VramUsageGb:0.0} / {m.VramTotalGb:0} GB";
        VramPercentage = (m.VramUsageGb / m.VramTotalGb) * 100.0;

        // Engine Status Indicators
        RendererText = m.Renderer;
        HardwareDecodeText = m.HardwareDecodeStatus;
        DwmCompositionText = m.IsDwmCompositionEnabled ? "Optimized" : "Disabled";
        WorkerWText = m.IsWorkerWAttached ? "Yes" : "No";
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
