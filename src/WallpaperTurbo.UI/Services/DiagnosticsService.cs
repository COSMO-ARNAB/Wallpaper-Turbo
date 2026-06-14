using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperTurbo.Core.Services.Performance;

namespace WallpaperTurbo.UI.Services;

/// <summary>
/// Development-time diagnostics service.
/// Tracks critical stability counters to detect:
///   - Container bloat (virtualization not working)
///   - BitmapSource accumulation (VRAM leak)
///   - Preview session leaks
///   - Decoder queue saturation
///   - Dispatcher callback queue pressure
///
/// All counters use Interlocked for thread-safe updates.
/// Properties are marshaled to UI thread for binding.
/// </summary>
public sealed class DiagnosticsService : ObservableObject
{
    // ─────────────────────────────────────────────────────
    // Singleton pattern (registered as DI singleton in App.xaml.cs)
    // ─────────────────────────────────────────────────────

    private static int _activeContainers;
    private static int _activeBitmaps;
    private static int _activePreviewSessions;
    private static int _decodeQueueDepth;
    private static int _dispatcherCallbacks;

    // ─────────────────────────────────────────────────────
    // Public UI-bound observable properties
    // ─────────────────────────────────────────────────────

    private int _activeContainersUI;
    private int _activeBitmapsUI;
    private int _activePreviewSessionsUI;
    private int _decodeQueueDepthUI;
    private int _dispatcherCallbacksUI;

    public int ActiveContainers         { get => _activeContainersUI;    private set => SetProperty(ref _activeContainersUI, value); }
    public int ActiveBitmaps            { get => _activeBitmapsUI;       private set => SetProperty(ref _activeBitmapsUI, value); }
    public int ActivePreviewSessions    { get => _activePreviewSessionsUI; private set => SetProperty(ref _activePreviewSessionsUI, value); }
    public int DecodeQueueDepth         { get => _decodeQueueDepthUI;    private set => SetProperty(ref _decodeQueueDepthUI, value); }
    public int DispatcherCallbacks      { get => _dispatcherCallbacksUI; private set => SetProperty(ref _dispatcherCallbacksUI, value); }

    private double _dispatcherLatency;
    public double DispatcherLatency
    {
        get => _dispatcherLatency;
        private set => SetProperty(ref _dispatcherLatency, value);
    }

    private string _memoryUsageText = string.Empty;
    public string MemoryUsageText
    {
        get => _memoryUsageText;
        private set => SetProperty(ref _memoryUsageText, value);
    }

    private double _gpuUsage;
    public double GpuUsage
    {
        get => _gpuUsage;
        private set => SetProperty(ref _gpuUsage, value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Two-Way Binding Wrappers for Subsystem Debug Flags
    // ─────────────────────────────────────────────────────────────────────────

    public bool EnableHoverPreviews
    {
        get => DebugFlags.EnableHoverPreviews;
        set
        {
            if (DebugFlags.EnableHoverPreviews != value)
            {
                DebugFlags.EnableHoverPreviews = value;
                OnPropertyChanged(nameof(EnableHoverPreviews));
                Debug.WriteLine($"[ISOLATE] EnableHoverPreviews set to {value}");
            }
        }
    }

    public bool EnableThumbnailEviction
    {
        get => DebugFlags.EnableThumbnailEviction;
        set
        {
            if (DebugFlags.EnableThumbnailEviction != value)
            {
                DebugFlags.EnableThumbnailEviction = value;
                OnPropertyChanged(nameof(EnableThumbnailEviction));
                Debug.WriteLine($"[ISOLATE] EnableThumbnailEviction set to {value}");
            }
        }
    }

    public bool EnableVirtualization
    {
        get => DebugFlags.EnableVirtualization;
        set
        {
            if (DebugFlags.EnableVirtualization != value)
            {
                DebugFlags.EnableVirtualization = value;
                OnPropertyChanged(nameof(EnableVirtualization));
                Debug.WriteLine($"[ISOLATE] EnableVirtualization set to {value}");
                // Force layout re-measure to update virtualization instantly
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        window.InvalidateMeasure();
                    }
                }, DispatcherPriority.Normal);
            }
        }
    }

    public bool EnableTelemetryInterpolation
    {
        get => DebugFlags.EnableTelemetryInterpolation;
        set
        {
            if (DebugFlags.EnableTelemetryInterpolation != value)
            {
                DebugFlags.EnableTelemetryInterpolation = value;
                OnPropertyChanged(nameof(EnableTelemetryInterpolation));
                Debug.WriteLine($"[ISOLATE] EnableTelemetryInterpolation set to {value}");
            }
        }
    }

    public bool EnableAsyncThumbnailLoading
    {
        get => DebugFlags.EnableAsyncThumbnailLoading;
        set
        {
            if (DebugFlags.EnableAsyncThumbnailLoading != value)
            {
                DebugFlags.EnableAsyncThumbnailLoading = value;
                OnPropertyChanged(nameof(EnableAsyncThumbnailLoading));
                Debug.WriteLine($"[ISOLATE] EnableAsyncThumbnailLoading set to {value}");
            }
        }
    }

    // Health indicator: true if counters look safe
    public bool IsHealthy =>
        ActiveContainers <= 30 &&
        ActiveBitmaps <= 120 &&
        ActivePreviewSessions <= 1 &&
        DecodeQueueDepth <= 3;

    // ─────────────────────────────────────────────────────
    // UI Hang Watchdog & Checkpoints
    // ─────────────────────────────────────────────────────

    private static string _lastKnownAction = "Application Started";
    public static string LastKnownAction
    {
        get => _lastKnownAction;
        private set => _lastKnownAction = value;
    }

    public static void SetAction(string action)
    {
        LastKnownAction = action;
        Debug.WriteLine($"[DIAG ACTION] {action}");
    }

    private readonly Thread _watchdogThread;

    private static void WatchdogLoop()
    {
        Debug.WriteLine("[UI Hang Watchdog] Watchdog thread started successfully.");
        
        while (true)
        {
            using var uiResponded = new ManualResetEventSlim(false);
            
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        uiResponded.Set();
                    }), DispatcherPriority.Send);
                }
                else
                {
                    Thread.Sleep(2000);
                    continue;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UI Hang Watchdog Error] Failed to post ping: {ex.Message}");
                Thread.Sleep(2000);
                continue;
            }

            // Wait up to 3 seconds for UI thread response
            bool responded = uiResponded.Wait(TimeSpan.FromSeconds(3));
            
            if (!responded)
            {
                string action = LastKnownAction;
                string hangMessage = $"[UI HUNG WATCHDOG 🚨🚨🚨] UI Thread is NOT RESPONDING! The UI Dispatcher has been frozen for over 3000 ms.\n" +
                                     $"Last known UI Thread Action: '{action}'\n" +
                                     $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}";
                
                Debug.WriteLine(hangMessage);
                Console.WriteLine(hangMessage);
                
                try
                {
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(logDir);
                    string logPath = Path.Combine(logDir, "ui-hang-report.txt");
                    File.WriteAllText(logPath, hangMessage);
                    Debug.WriteLine($"[UI Hang Watchdog] Saved report to {logPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UI Hang Watchdog] Failed to save log file: {ex.Message}");
                }

                Thread.Sleep(5000);
            }
            else
            {
                Thread.Sleep(1000);
            }
        }
    }

    // ─────────────────────────────────────────────────────
    // Refresh timer
    // ─────────────────────────────────────────────────────

    private readonly DispatcherTimer _refreshTimer;

    public DiagnosticsService()
    {
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        Debug.WriteLine("[DiagnosticsService] Started. Tracking containers, bitmaps, previews, decoders.");

        // Start background watchdog thread
        _watchdogThread = new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "UI-Hang-Watchdog"
        };
        _watchdogThread.Start();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        // Read atomics and push to observable props (already on UI thread via DispatcherTimer)
        int c = Interlocked.CompareExchange(ref _activeContainers, 0, 0);
        int b = Interlocked.CompareExchange(ref _activeBitmaps, 0, 0);
        int p = Interlocked.CompareExchange(ref _activePreviewSessions, 0, 0);
        int d = Interlocked.CompareExchange(ref _decodeQueueDepth, 0, 0);
        int dc = Interlocked.CompareExchange(ref _dispatcherCallbacks, 0, 0);

        if (c != _activeContainersUI)  ActiveContainers       = c;
        if (b != _activeBitmapsUI)     ActiveBitmaps          = b;
        if (p != _activePreviewSessionsUI) ActivePreviewSessions = p;
        if (d != _decodeQueueDepthUI)  DecodeQueueDepth       = d;
        if (dc != _dispatcherCallbacksUI) DispatcherCallbacks  = dc;

        OnPropertyChanged(nameof(IsHealthy));

        // ── Measure Dispatcher Latency ──
        var sw = Stopwatch.StartNew();
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            sw.Stop();
            DispatcherLatency = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        }), DispatcherPriority.Background);

        // ── Measure Memory Usage ──
        try
        {
            double managedMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            double privateMemoryMb;
            using (var currentProcess = Process.GetCurrentProcess())
            {
                privateMemoryMb = currentProcess.PrivateMemorySize64 / (1024.0 * 1024.0);
            }
            MemoryUsageText = $"Managed: {managedMemoryMb:F1} MB | Private: {privateMemoryMb:F1} MB";
        }
        catch
        {
            MemoryUsageText = "N/A";
        }

        MemoryLogger.LogMemoryStats("UI");

        // ── Read GPU Usage ──
        try
        {
            var telemetry = App.GetService<TelemetryService>();
            if (telemetry != null)
            {
                GpuUsage = telemetry.CurrentMetrics.GpuUsage;
            }
        }
        catch
        {
            GpuUsage = 0.0;
        }

        // ── Raise changes for dynamic toggles so bindings always reflect correct state ──
        OnPropertyChanged(nameof(EnableHoverPreviews));
        OnPropertyChanged(nameof(EnableThumbnailEviction));
        OnPropertyChanged(nameof(EnableVirtualization));
        OnPropertyChanged(nameof(EnableTelemetryInterpolation));
        OnPropertyChanged(nameof(EnableAsyncThumbnailLoading));

#if DEBUG
        if (!IsHealthy)
        {
            Debug.WriteLine($"[DIAG ⚠️] Containers={c} Bitmaps={b} Previews={p} DecodeQ={d} DispCallbacks={dc}");
        }
#endif
    }

    // ─────────────────────────────────────────────────────
    // Static counter manipulation (call from any thread)
    // ─────────────────────────────────────────────────────

    public static void OnContainerRealized()   => Interlocked.Increment(ref _activeContainers);
    public static void OnContainerRecycled()   => Interlocked.Decrement(ref _activeContainers);
    public static void ResetContainerCount(int n) => Interlocked.Exchange(ref _activeContainers, n);

    public static void OnBitmapLoaded()        => Interlocked.Increment(ref _activeBitmaps);
    public static void OnBitmapEvicted()       => Interlocked.Decrement(ref _activeBitmaps);

    public static void OnPreviewStarted()      => Interlocked.Increment(ref _activePreviewSessions);
    public static void OnPreviewStopped()      => Interlocked.Decrement(ref _activePreviewSessions);

    public static void OnDecodeQueued()        => Interlocked.Increment(ref _decodeQueueDepth);
    public static void OnDecodeCompleted()     => Interlocked.Decrement(ref _decodeQueueDepth);

    public static void OnDispatcherCallbackQueued()    => Interlocked.Increment(ref _dispatcherCallbacks);
    public static void OnDispatcherCallbackCompleted() => Interlocked.Decrement(ref _dispatcherCallbacks);

    // ─────────────────────────────────────────────────────
    // Snapshot: call this from stress test or on-demand
    // ─────────────────────────────────────────────────────

    public void LogSnapshot()
    {
        Debug.WriteLine(
            $"[DIAG SNAPSHOT] " +
            $"Containers={_activeContainersUI} " +
            $"Bitmaps={_activeBitmapsUI} " +
            $"Previews={_activePreviewSessionsUI} " +
            $"DecodeQ={_decodeQueueDepthUI} " +
            $"DispCallbacks={_dispatcherCallbacksUI} " +
            $"Health={IsHealthy}");
    }

    public void Stop()
    {
        _refreshTimer.Stop();
    }
}
