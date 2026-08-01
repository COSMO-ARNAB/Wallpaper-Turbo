using System;
using System.Diagnostics;

namespace WallpaperTurbo.UI.Services;

public class FallbackTelemetryProvider : ITelemetryProvider
{
    private DateTime _lastCpuTime = DateTime.UtcNow;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

    public bool IsSupported => true;

    public bool Initialize(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            _lastTotalProcessorTime = process.TotalProcessorTime;
            _lastCpuTime = DateTime.UtcNow;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Poll(int pid, TelemetryMetrics metrics)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Refresh();

            // 1. Calculate CPU usage dynamically from process processor time
            var now = DateTime.UtcNow;
            var cpuTime = process.TotalProcessorTime;
            var timeWindow = now - _lastCpuTime;
            
            if (timeWindow.TotalMilliseconds > 0)
            {
                var cpuUsage = (cpuTime - _lastTotalProcessorTime).TotalMilliseconds / (Environment.ProcessorCount * timeWindow.TotalMilliseconds) * 100.0;
                metrics.CpuUsage = Math.Round(Math.Clamp(cpuUsage, 0.0, 100.0), 1);
                metrics.IsCpuAvailable = true;
            }

            _lastCpuTime = now;
            _lastTotalProcessorTime = cpuTime;

            // 2. RAM Usage
            metrics.RamUsageGb = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0 * 1024.0), 2);
            metrics.IsRamAvailable = true;
            metrics.GpuUsage = 0;
            metrics.IsGpuAvailable = false;
            metrics.VideoDecodeUsage = 0;
            metrics.IsVideoDecodeAvailable = false;
            metrics.VramUsageGb = 0;
            metrics.IsVramAvailable = false;
        }
        catch
        {
            // Process terminated or inaccessible
            metrics.GpuUsage = 0.0;
            metrics.IsGpuAvailable = false;
            metrics.VideoDecodeUsage = 0.0;
            metrics.IsVideoDecodeAvailable = false;
            metrics.VramUsageGb = 0.0;
            metrics.IsVramAvailable = false;
            metrics.CpuUsage = 0.0;
            metrics.IsCpuAvailable = false;
            metrics.RamUsageGb = 0.0;
            metrics.IsRamAvailable = false;
        }
    }

    public void Reset()
    {
    }

    public void Dispose()
    {
    }
}
