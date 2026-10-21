using System;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class TelemetryViewModel : ObservableObject, IDisposable
{
    private readonly Action? _unsubscribe;
    private bool _disposed;

    public TelemetryViewModel(Action? unsubscribe = null)
    {
        _unsubscribe = unsubscribe;
    }
    [ObservableProperty] private bool _isEngineRunning;
    [ObservableProperty] private bool _isCpuAvailable;
    [ObservableProperty] private bool _isGpuAvailable;
    [ObservableProperty] private bool _isVideoDecodeAvailable;
    [ObservableProperty] private bool _isGpu3DAssistAvailable;
    [ObservableProperty] private bool _isRamAvailable;
    [ObservableProperty] private bool _isVramAvailable;
    [ObservableProperty] private bool _isFpsAvailable;
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _gpuUsage;
    [ObservableProperty] private double _videoDecodeUsage;
    [ObservableProperty] private double _gpu3DAssistUsage;
    [ObservableProperty] private double _ramUsageGb;
    [ObservableProperty] private double _ramTotalGb;
    [ObservableProperty] private double _vramUsageGb;
    [ObservableProperty] private double _vramTotalGb;
    [ObservableProperty] private int _fps;
    [ObservableProperty] private string _uptimeText = "00:00:00";
    [ObservableProperty] private string _renderer = "None";

    public string CpuText => IsCpuAvailable ? $"{CpuUsage:0.0}%" : "N/A";
    public string GpuText => IsGpuAvailable ? $"{GpuUsage:0.0}%" : "N/A";
    public string VideoDecodeText => IsVideoDecodeAvailable ? $"{VideoDecodeUsage:0.0}%" : "N/A";
    public string Gpu3DAssistText => IsGpu3DAssistAvailable ? $"{Gpu3DAssistUsage:0.0}%" : "N/A";
    public string RamText => IsRamAvailable ? $"{RamUsageGb:0.00} GB" : "N/A";
    public string VramText => IsVramAvailable ? $"{VramUsageGb:0.00} GB" : "N/A";
    public string FpsText => IsFpsAvailable ? Fps.ToString() : "N/A";

    public void Update(TelemetryMetrics metrics, bool isEngineRunning)
    {
        IsEngineRunning = isEngineRunning;
        IsCpuAvailable = isEngineRunning && metrics.IsCpuAvailable;
        IsGpuAvailable = isEngineRunning && metrics.IsGpuAvailable;
        IsVideoDecodeAvailable = isEngineRunning && metrics.IsVideoDecodeAvailable;
        IsGpu3DAssistAvailable = isEngineRunning && metrics.IsGpu3DAssistAvailable;
        IsRamAvailable = isEngineRunning && metrics.IsRamAvailable;
        IsVramAvailable = isEngineRunning && metrics.IsVramAvailable;
        IsFpsAvailable = isEngineRunning && metrics.IsFpsAvailable;
        CpuUsage = metrics.CpuUsage;
        GpuUsage = metrics.GpuUsage;
        VideoDecodeUsage = metrics.VideoDecodeUsage;
        Gpu3DAssistUsage = metrics.Gpu3DAssistUsage;
        RamUsageGb = metrics.RamUsageGb;
        RamTotalGb = metrics.RamTotalGb;
        VramUsageGb = metrics.VramUsageGb;
        VramTotalGb = metrics.VramTotalGb;
        Fps = metrics.Fps;
        UptimeText = isEngineRunning
            ? $"{(int)metrics.Uptime.TotalHours:00}:{metrics.Uptime.Minutes:00}:{metrics.Uptime.Seconds:00}"
            : "00:00:00";
        Renderer = isEngineRunning ? metrics.Renderer : "None";

        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(GpuText));
        OnPropertyChanged(nameof(VideoDecodeText));
        OnPropertyChanged(nameof(Gpu3DAssistText));
        OnPropertyChanged(nameof(RamText));
        OnPropertyChanged(nameof(VramText));
        OnPropertyChanged(nameof(FpsText));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe?.Invoke();
    }
}
