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

    private class GpuCounterInfo
    {
        public string Luid { get; set; } = "";
        public string EngineType { get; set; } = ""; // 3d, videodecode, copy
        public PerformanceCounter Counter { get; set; } = null!;
    }

    private class VramCounterInfo
    {
        public string Luid { get; set; } = "";
        public PerformanceCounter Counter { get; set; } = null!;
    }

    public bool IsSupported => _isSupported;

    public bool Initialize(int pid)
    {
        if (pid == _initializedPid && _isSupported)
            return true;

        Reset();

        try
        {
            // Initialize new GPU Engine counters for this process
            var engineCategory = new PerformanceCounterCategory("GPU Engine");
            string[] engineInstances = engineCategory.GetInstanceNames();
            var processInstances = engineInstances.Where(i => i.StartsWith($"pid_{pid}_", StringComparison.OrdinalIgnoreCase));

            foreach (var instance in processInstances)
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

                if (engtype == "3d" || engtype == "videodecode" || engtype == "copy")
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                    _gpuCounters.Add(new GpuCounterInfo
                    {
                        Luid = luid,
                        EngineType = engtype,
                        Counter = counter
                    });
                }
            }

            // Initialize new VRAM counters for this process
            var memCategory = new PerformanceCounterCategory("GPU Process Memory");
            string[] memInstances = memCategory.GetInstanceNames();
            var memProcessInstances = memInstances.Where(i => i.StartsWith($"pid_{pid}_", StringComparison.OrdinalIgnoreCase));

            foreach (var instance in memProcessInstances)
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
                    Luid = luid,
                    Counter = counter
                });
            }

            _initializedPid = pid;
            _isSupported = _gpuCounters.Count > 0;
            return _isSupported;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize GPU performance counters: {ex.Message}");
            _isSupported = false;
            return false;
        }
    }

    public void Poll(int pid, TelemetryMetrics metrics)
    {
        if (!_isSupported || pid != _initializedPid)
            return;

        try
        {
            var gpuValues = new Dictionary<string, (double Gpu3D, double GpuDecode)>();
            foreach (var info in _gpuCounters)
            {
                try
                {
                    float val = info.Counter.NextValue();
                    if (!gpuValues.ContainsKey(info.Luid))
                    {
                        gpuValues[info.Luid] = (0.0, 0.0);
                    }

                    var current = gpuValues[info.Luid];
                    if (info.EngineType == "3d")
                    {
                        current.Gpu3D = val;
                    }
                    else if (info.EngineType == "videodecode")
                    {
                        current.GpuDecode = val;
                    }
                    gpuValues[info.Luid] = current;
                }
                catch { }
            }

            string activeLuid = "";
            double maxDecode = -1.0;
            double max3D = -1.0;

            foreach (var kvp in gpuValues)
            {
                if (kvp.Value.GpuDecode > maxDecode)
                {
                    maxDecode = kvp.Value.GpuDecode;
                    activeLuid = kvp.Key;
                }
            }

            if (maxDecode <= 0)
            {
                foreach (var kvp in gpuValues)
                {
                    if (kvp.Value.Gpu3D > max3D)
                    {
                        max3D = kvp.Value.Gpu3D;
                        activeLuid = kvp.Key;
                    }
                }
            }

            if (string.IsNullOrEmpty(activeLuid) && gpuValues.Count > 0)
            {
                activeLuid = gpuValues.Keys.First();
            }

            if (!string.IsNullOrEmpty(activeLuid) && gpuValues.ContainsKey(activeLuid))
            {
                var activeMetrics = gpuValues[activeLuid];
                metrics.GpuUsage = Math.Round(activeMetrics.Gpu3D, 1);
                metrics.VideoDecodeUsage = Math.Round(activeMetrics.GpuDecode, 1);

                double vramGb = 0.0;
                var vramInfo = _vramCounters.FirstOrDefault(x => x.Luid == activeLuid);
                if (vramInfo != null)
                {
                    try
                    {
                        vramGb = vramInfo.Counter.NextValue() / (1024.0 * 1024.0 * 1024.0);
                    }
                    catch { }
                }
                metrics.VramUsageGb = Math.Round(vramGb, 2);
            }
            else
            {
                metrics.GpuUsage = 0.0;
                metrics.VideoDecodeUsage = 0.0;
                metrics.VramUsageGb = 0.0;
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
