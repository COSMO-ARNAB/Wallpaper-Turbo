using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WallpaperTurbo.UI.Services;

public class TelemetryMetrics
{
    public double CpuUsage { get; set; }
    public bool IsCpuAvailable { get; set; }
    public double GpuUsage { get; set; }
    public bool IsGpuAvailable { get; set; }
    public double VideoDecodeUsage { get; set; }
    public bool IsVideoDecodeAvailable { get; set; }
    public double RamUsageGb { get; set; }
    public bool IsRamAvailable { get; set; }
    public double RamTotalGb { get; set; }
    public double VramUsageGb { get; set; }
    public bool IsVramAvailable { get; set; }
    public double VramTotalGb { get; set; }
    public int Fps { get; set; }
    public bool IsFpsAvailable { get; set; }
    public TimeSpan Uptime { get; set; } = TimeSpan.Zero;
    public bool IsWorkerWAttached { get; set; }
    public bool IsDwmCompositionAvailable { get; set; }
    public bool IsDwmCompositionEnabled { get; set; } = true;
    public string Renderer { get; set; } = "None";
    public string HardwareDecodeStatus { get; set; } = "Inactive";
}

public class TelemetryService
{
    private readonly System.Timers.Timer _timer;
    private readonly TelemetryMetrics _metrics = new();
    private readonly object _metricsLock = new();
    private DateTime _startTime = DateTime.UtcNow;

    private int _lastAppRunnerPid = -1;
    private ITelemetryProvider _provider = null!;
    private int _fallbackPolls;

    // Win32 Helpers for WorkerW & DWM
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmIsCompositionEnabled(out bool pfEnabled);

    public event Action<TelemetryMetrics>? MetricsUpdated;

    public TelemetryService()
    {
        // Fetch total physical memory once
        try
        {
            _metrics.RamTotalGb = Math.Round(GetTotalPhysicalMemoryBytes() / (1024.0 * 1024.0 * 1024.0), 1);
            if (_metrics.RamTotalGb <= 0)
                _metrics.RamTotalGb = 0;
        }
        catch
        {
            _metrics.RamTotalGb = 0;
        }

        // Initialize default provider (attempt PerformanceCounters first)
        _provider = new PerformanceCounterTelemetryProvider();

        _timer = new System.Timers.Timer(1000);
        _timer.AutoReset = false;
        _timer.Elapsed += async (s, e) =>
        {
            try
            {
                await PollMetricsAsync();
            }
            finally
            {
                _timer.Start();
            }
        };
    }

    public void Start()
    {
        StartupDiagnostics.LogWithMemory("TelemetryService START");
        _startTime = DateTime.UtcNow;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _provider?.Reset();
    }

    public TelemetryMetrics CurrentMetrics
    {
        get
        {
            lock (_metricsLock)
            {
                return CopyMetrics(_metrics);
            }
        }
    }

    private static TelemetryMetrics CopyMetrics(TelemetryMetrics source)
    {
        return new TelemetryMetrics
        {
            CpuUsage = source.CpuUsage,
            IsCpuAvailable = source.IsCpuAvailable,
            GpuUsage = source.GpuUsage,
            IsGpuAvailable = source.IsGpuAvailable,
            VideoDecodeUsage = source.VideoDecodeUsage,
            IsVideoDecodeAvailable = source.IsVideoDecodeAvailable,
            RamUsageGb = source.RamUsageGb,
            IsRamAvailable = source.IsRamAvailable,
            RamTotalGb = source.RamTotalGb,
            VramUsageGb = source.VramUsageGb,
            IsVramAvailable = source.IsVramAvailable,
            VramTotalGb = source.VramTotalGb,
            Fps = source.Fps,
            IsFpsAvailable = source.IsFpsAvailable,
            Uptime = source.Uptime,
            IsWorkerWAttached = source.IsWorkerWAttached,
            IsDwmCompositionAvailable = source.IsDwmCompositionAvailable,
            IsDwmCompositionEnabled = source.IsDwmCompositionEnabled,
            Renderer = source.Renderer,
            HardwareDecodeStatus = source.HardwareDecodeStatus
        };
    }

    private async Task PollMetricsAsync()
    {
        if (DebugFlags.SafeDebugMode)
        {
            lock (_metricsLock)
            {
                ResetProcessMetrics(_metrics);
            }
            PublishSnapshot();
            return;
        }

        Process[]? runnerProcesses = null;
        try
        {
            // 1. Detect if AppRunner process is active and retrieve its uptime
            runnerProcesses = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
            Process? runner = runnerProcesses.FirstOrDefault(p => !p.HasExited);

            bool isRunning = runner != null;
            if (isRunning && runner != null)
            {
                try
                {
                lock (_metricsLock)
                {
                    _metrics.Uptime = DateTime.Now - runner.StartTime;
                }
                }
                catch
                {
                    lock (_metricsLock)
                    {
                        _metrics.Uptime = DateTime.UtcNow - _startTime;
                    }
                }
                
                // Read memory working set (basic process metric)
                try
                {
                    runner.Refresh();
                    lock (_metricsLock)
                    {
                        _metrics.RamUsageGb = Math.Round(runner.WorkingSet64 / (1024.0 * 1024.0 * 1024.0), 2);
                        _metrics.IsRamAvailable = true;
                    }
                }
                catch
                {
                    lock (_metricsLock)
                    {
                        _metrics.RamUsageGb = 0;
                        _metrics.IsRamAvailable = false;
                    }
                }
            }
            else
            {
                lock (_metricsLock)
                {
                    _metrics.Uptime = TimeSpan.Zero;
                    _metrics.RamUsageGb = 0;
                    _metrics.IsRamAvailable = false;
                }
            }

            // 2. Fetch GPU Performance Metrics dynamically via Windows Performance Counters or Fallback
            await Task.Run(() =>
            {
                if (isRunning && runner != null)
                {
                    int pid = runner.Id;
                    
                    // Initialize or update Performance Counters if PID has changed
                    if (pid != _lastAppRunnerPid)
                    {
                        _lastAppRunnerPid = pid;
                        // Try primary PerformanceCounter provider
                        bool success = false;
                        try
                        {
                            if (_provider is not PerformanceCounterTelemetryProvider)
                            {
                                _provider?.Dispose();
                                _provider = new PerformanceCounterTelemetryProvider();
                            }
                            success = _provider.Initialize(pid);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"PerformanceCounter provider throws on init: {ex.Message}");
                        }

                        // If primary provider fails or is not supported, switch automatically to Fallback provider
                        if (!success || !_provider.IsSupported)
                        {
                            Debug.WriteLine("Switching to FallbackTelemetryProvider due to counter load failure.");
                            _provider?.Dispose();
                            _provider = new FallbackTelemetryProvider();
                            _provider.Initialize(pid);
                            _fallbackPolls = 0;
                        }
                    }

                    if (_provider is FallbackTelemetryProvider && ++_fallbackPolls >= 5)
                    {
                        var primary = new PerformanceCounterTelemetryProvider();
                        if (primary.Initialize(pid))
                        {
                            _provider.Dispose();
                            _provider = primary;
                        }
                        else
                        {
                            primary.Dispose();
                        }
                        _fallbackPolls = 0;
                    }

                    // Poll using the active provider
                    try
                    {
                        lock (_metricsLock)
                        {
                            _provider.Poll(pid, _metrics);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error polling telemetry provider: {ex.Message}");
                        // Force a fallback switch next loop
                        _lastAppRunnerPid = -1;
                        _fallbackPolls = 0;
                    }

                    // FPS and renderer details require renderer-side instrumentation.
                    // Never present metadata or assumed values as live measurements.
                    lock (_metricsLock)
                    {
                        _metrics.Fps = 0;
                        _metrics.IsFpsAvailable = false;
                        _metrics.Renderer = "Unavailable";
                        _metrics.HardwareDecodeStatus = "Unavailable";
                    }
                }
                else
                {
                    // Clean up and clear counter cache in idle state
                    if (_lastAppRunnerPid != -1)
                    {
                        _provider?.Reset();
                        _lastAppRunnerPid = -1;
                        _fallbackPolls = 0;
                    }

                    // Idle metrics
                    lock (_metricsLock)
                    {
                        ResetProcessMetrics(_metrics);
                    }
                }

                // 3. WorkerW desktop attachment check
                try
                {
                    IntPtr renderWindow = FindWindow("WallpaperTurbo_RenderWindow_Class", null);
                    lock (_metricsLock)
                    {
                        _metrics.IsWorkerWAttached = renderWindow != IntPtr.Zero;
                    }
                }
                catch
                {
                    lock (_metricsLock)
                    {
                        _metrics.IsWorkerWAttached = false;
                    }
                }

                // 4. DWM Composition check
                try
                {
                    DwmIsCompositionEnabled(out bool dwmEnabled);
                    lock (_metricsLock)
                    {
                        _metrics.IsDwmCompositionEnabled = dwmEnabled;
                        _metrics.IsDwmCompositionAvailable = true;
                    }
                }
                catch
                {
                    lock (_metricsLock)
                    {
                        _metrics.IsDwmCompositionEnabled = false;
                        _metrics.IsDwmCompositionAvailable = false;
                    }
                }
            });

            // Always marshal MetricsUpdated to the UI dispatcher.
            // TelemetryService fires from System.Timers.Timer (background thread).
            // ObservableObject.SetProperty raises PropertyChanged; WPF bindings require UI thread.
            PublishSnapshot();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error polling telemetry metrics: {ex.Message}");
        }
        finally
        {
            if (runnerProcesses != null)
            {
                foreach (var p in runnerProcesses)
                {
                    p.Dispose();
                }
            }
        }
    }

    private void PublishSnapshot()
    {
        var snapshot = CurrentMetrics;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            MetricsUpdated?.Invoke(snapshot);
            return;
        }

        dispatcher.BeginInvoke(
            new Action(() => MetricsUpdated?.Invoke(snapshot)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void ResetProcessMetrics(TelemetryMetrics metrics)
    {
        metrics.CpuUsage = 0;
        metrics.IsCpuAvailable = false;
        metrics.GpuUsage = 0;
        metrics.IsGpuAvailable = false;
        metrics.VideoDecodeUsage = 0;
        metrics.IsVideoDecodeAvailable = false;
        metrics.RamUsageGb = 0;
        metrics.IsRamAvailable = false;
        metrics.VramUsageGb = 0;
        metrics.IsVramAvailable = false;
        metrics.Fps = 0;
        metrics.IsFpsAvailable = false;
        metrics.Uptime = TimeSpan.Zero;
        metrics.Renderer = "None";
        metrics.HardwareDecodeStatus = "Inactive";
        metrics.IsWorkerWAttached = false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private static ulong GetTotalPhysicalMemoryBytes()
    {
        var memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            return memStatus.ullTotalPhys;
        }
        return 0;
    }
}
