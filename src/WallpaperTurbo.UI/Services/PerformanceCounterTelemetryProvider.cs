using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WallpaperTurbo.UI.Services;

public class PerformanceCounterTelemetryProvider : ITelemetryProvider
{
    private int _initializedPid = -1;
    private readonly List<GpuCounterInfo> _gpuCounters = new();
    private readonly List<VramCounterInfo> _vramCounters = new();
    private bool _isSupported = true;
    private DateTime _lastCpuSampleUtc;
    private TimeSpan _lastProcessorTime;
    private DateTime _lastCounterDiscoveryUtc;

    private class GpuCounterInfo
    {
        public string InstanceName { get; set; } = "";
        public string EngineId { get; set; } = "";
        public string EngineType { get; set; } = ""; // 3d, videodecode, copy
        public PerformanceCounter Counter { get; set; } = null!;
    }

    private class VramCounterInfo
    {
        public string InstanceName { get; set; } = "";
        public string Luid { get; set; } = "";
        public PerformanceCounter Counter { get; set; } = null!;
    }

    public bool IsSupported => _isSupported;

    public bool Initialize(int pid)
    {
        if (pid == _initializedPid && _isSupported &&
            DateTime.UtcNow - _lastCounterDiscoveryUtc < TimeSpan.FromSeconds(5))
            return true;

        try
        {
            if (pid != _initializedPid)
            {
                Reset();
                using var process = Process.GetProcessById(pid);
                _lastProcessorTime = process.TotalProcessorTime;
                _lastCpuSampleUtc = DateTime.UtcNow;
                _initializedPid = pid;
            }

            RefreshCounters(pid);
            _isSupported = _gpuCounters.Count > 0;
            _lastCounterDiscoveryUtc = DateTime.UtcNow;
            return _isSupported;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize GPU performance counters: {ex.Message}");
            _isSupported = false;
            return false;
        }
    }

    private void RefreshCounters(int pid)
    {
            // GPU Engine exposes one instance per process/physical engine pair.
            // Reading all instances and grouping out the PID yields system-wide usage.
            var engineCategory = new PerformanceCounterCategory("GPU Engine");
            string[] engineInstances = engineCategory.GetInstanceNames();
            var processEngineInstances = engineInstances
                .Where(i => i.StartsWith($"pid_{pid}_", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var currentEngineInstances = processEngineInstances.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stale in _gpuCounters.Where(info => !currentEngineInstances.Contains(info.InstanceName)).ToArray())
            {
                stale.Counter.Dispose();
                _gpuCounters.Remove(stale);
            }

            foreach (var instance in processEngineInstances.Where(instance =>
                         !_gpuCounters.Any(info => string.Equals(info.InstanceName, instance, StringComparison.OrdinalIgnoreCase))))
            {
                string[] parts = instance.Split('_');
                int luidIndex = Array.IndexOf(parts, "luid");
                string luid = "";
                if (luidIndex >= 0 && luidIndex + 2 < parts.Length)
                {
                    luid = parts[luidIndex + 1] + "_" + parts[luidIndex + 2];
                }

                int engtypeIndex = Array.IndexOf(parts, "engtype");
                string engtype = "";
                if (engtypeIndex >= 0 && engtypeIndex + 1 < parts.Length)
                {
                    engtype = parts[engtypeIndex + 1].ToLowerInvariant();
                }

                int physicalIndex = Array.IndexOf(parts, "phys");
                string physical = physicalIndex >= 0 && physicalIndex + 1 < parts.Length
                    ? parts[physicalIndex + 1]
                    : "unknown";
                int engineIndex = Array.IndexOf(parts, "eng");
                string engine = engineIndex >= 0 && engineIndex + 1 < parts.Length
                    ? parts[engineIndex + 1]
                    : instance;

                if (!string.IsNullOrEmpty(engtype))
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                    _gpuCounters.Add(new GpuCounterInfo
                    {
                        InstanceName = instance,
                        EngineId = $"{luid}|{physical}|{engine}|{engtype}",
                        EngineType = engtype,
                        Counter = counter
                    });
                }
            }

            // Initialize new VRAM counters for this process
            var memCategory = new PerformanceCounterCategory("GPU Process Memory");
            string[] memInstances = memCategory.GetInstanceNames();
            var memProcessInstances = memInstances
                .Where(i => i.StartsWith($"pid_{pid}_", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var currentMemoryInstances = memProcessInstances.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stale in _vramCounters.Where(info => !currentMemoryInstances.Contains(info.InstanceName)).ToArray())
            {
                stale.Counter.Dispose();
                _vramCounters.Remove(stale);
            }

            foreach (var instance in memProcessInstances.Where(instance =>
                         !_vramCounters.Any(info => string.Equals(info.InstanceName, instance, StringComparison.OrdinalIgnoreCase))))
            {
                string[] parts = instance.Split('_');
                int luidIndex = Array.IndexOf(parts, "luid");
                string luid = "";
                if (luidIndex >= 0 && luidIndex + 2 < parts.Length)
                {
                    luid = parts[luidIndex + 1] + "_" + parts[luidIndex + 2];
                }

                var counter = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", instance, true);
                _vramCounters.Add(new VramCounterInfo
                {
                    InstanceName = instance,
                    Luid = luid,
                    Counter = counter
                });
            }
    }

    public void Poll(int pid, TelemetryMetrics metrics)
    {
        if (!_isSupported || pid != _initializedPid)
            return;

        try
        {
            if (DateTime.UtcNow - _lastCounterDiscoveryUtc >= TimeSpan.FromSeconds(5) && !Initialize(pid))
            {
                return;
            }

            using (var process = Process.GetProcessById(pid))
            {
                process.Refresh();
                var now = DateTime.UtcNow;
                var processorTime = process.TotalProcessorTime;
                var elapsed = now - _lastCpuSampleUtc;
                if (elapsed.TotalMilliseconds > 0)
                {
                    var cpu = (processorTime - _lastProcessorTime).TotalMilliseconds /
                              (Environment.ProcessorCount * elapsed.TotalMilliseconds) * 100.0;
                    metrics.CpuUsage = Math.Round(Math.Clamp(cpu, 0.0, 100.0), 1);
                    metrics.IsCpuAvailable = true;
                }
                metrics.RamUsageGb = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0 * 1024.0), 2);
                metrics.IsRamAvailable = true;
                _lastProcessorTime = processorTime;
                _lastCpuSampleUtc = now;
            }

            var engineValues = new Dictionary<string, (string EngineType, double Usage)>();
            foreach (var info in _gpuCounters)
            {
                try
                {
                    float val = info.Counter.NextValue();
                    engineValues.TryGetValue(info.EngineId, out var current);
                    engineValues[info.EngineId] = (info.EngineType, current.Usage + val);
                }
                catch { }
            }

            if (engineValues.Count > 0)
            {
                // Task Manager's overall GPU percentage is the busiest physical engine,
                // after each engine's per-process instances have been summed.
                double totalGpu = engineValues.Values.Max(value => value.Usage);
                var decodeEngines = engineValues.Values
                    .Where(value => value.EngineType == "videodecode")
                    .Select(value => value.Usage)
                    .ToArray();
                metrics.GpuUsage = Math.Round(Math.Clamp(totalGpu, 0, 100), 1);
                metrics.IsGpuAvailable = true;
                metrics.VideoDecodeUsage = decodeEngines.Length > 0
                    ? Math.Round(Math.Clamp(decodeEngines.Max(), 0, 100), 1)
                    : 0;
                metrics.IsVideoDecodeAvailable = decodeEngines.Length > 0;

                long dedicatedBytes = 0;
                foreach (var vramInfo in _vramCounters)
                {
                    try
                    {
                        dedicatedBytes += (long)vramInfo.Counter.NextValue();
                    }
                    catch { }
                }
                metrics.VramUsageGb = Math.Round(dedicatedBytes / (1024.0 * 1024.0 * 1024.0), 2);
                metrics.IsVramAvailable = _vramCounters.Count > 0;
            }
            else
            {
                metrics.GpuUsage = 0.0;
                metrics.IsGpuAvailable = false;
                metrics.VideoDecodeUsage = 0.0;
                metrics.IsVideoDecodeAvailable = false;
                metrics.VramUsageGb = 0.0;
                metrics.IsVramAvailable = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error polling performance counters: {ex.Message}");
        }
    }

    public void Reset()
    {
        foreach (var info in _gpuCounters)
        {
            try { info.Counter.Dispose(); } catch { }
        }
        _gpuCounters.Clear();

        foreach (var info in _vramCounters)
        {
            try { info.Counter.Dispose(); } catch { }
        }
        _vramCounters.Clear();

        _initializedPid = -1;
    }

    public void Dispose()
    {
        Reset();
    }
}
