using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

namespace WallpaperTurbo.UI.Services;

public static class StartupDiagnostics
{
    private static readonly object _lock = new();
    private static bool _isInitialized = false;
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperTurbo",
        "Logs",
        "startup-diagnostics.log");
    private static readonly Dictionary<string, Stopwatch> _stopwatches = new();

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                // Open file with FileShare.ReadWrite and write empty to overwrite
                using (var fs = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(fs))
                {
                    writer.Write("");
                    writer.Flush();
                }
                
                _isInitialized = true;
                
                // Register exception handlers
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    LogException("AppDomain.CurrentDomain.UnhandledException", e.ExceptionObject as Exception);
                };

                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    LogException("TaskScheduler.UnobservedTaskException", e.Exception);
                    e.SetObserved();
                };
                
                LogWithMemory("Startup Diagnostics Initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAGNOSTICS_ERROR] Initialize failed: {ex.Message}");
            }
        }
    }

    public static void Log(string message)
    {
        lock (_lock)
        {
            try
            {
                // Ensure initialized (in case it is called before Initialize)
                if (!_isInitialized)
                {
                    var dir = Path.GetDirectoryName(LogFilePath);
                    if (dir != null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    _isInitialized = true;
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string threadId = $"[T{Environment.CurrentManagedThreadId}]";
                string logLine = $"{timestamp} {threadId} {message}";

                // Write and flush immediately
                using (var fs = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(fs))
                {
                    writer.WriteLine(logLine);
                    writer.Flush();
                    // Flush to OS disk
                    fs.Flush(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAGNOSTICS_ERROR] Failed to log: {ex.Message}");
            }
        }
    }

    public static void LogWithMemory(string message)
    {
        try
        {
            using (var process = Process.GetCurrentProcess())
            {
                double wsMb = process.WorkingSet64 / (1024.0 * 1024.0);
                double pmMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);
                Log($"{message} [Memory: WorkingSet={wsMb:F2}MB, PrivateMemory={pmMb:F2}MB]");
            }
        }
        catch (Exception ex)
        {
            Log($"{message} [Memory: Failed to retrieve: {ex.Message}]");
        }
    }

    public static void LogException(string context, Exception? ex)
    {
        if (ex != null)
        {
            Log($"[UNHANDLED EXCEPTION] {context}: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Log($"[INNER EXCEPTION] {ex.InnerException.GetType().Name} - {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}");
            }
        }
        else
        {
            Log($"[UNHANDLED EXCEPTION] {context}: (null exception object)");
        }
    }

    public static void StartTimer(string name)
    {
        lock (_lock)
        {
            var sw = Stopwatch.StartNew();
            _stopwatches[name] = sw;
            Log($"{name} START");
        }
    }

    public static void StopTimer(string name)
    {
        lock (_lock)
        {
            if (_stopwatches.TryGetValue(name, out var sw))
            {
                sw.Stop();
                Log($"{name} END (Duration: {sw.ElapsedMilliseconds} ms)");
                _stopwatches.Remove(name);
            }
            else
            {
                Log($"{name} END (Duration: unknown)");
            }
        }
    }

    public static void StopTimerWithMemory(string name)
    {
        lock (_lock)
        {
            if (_stopwatches.TryGetValue(name, out var sw))
            {
                sw.Stop();
                LogWithMemory($"{name} END (Duration: {sw.ElapsedMilliseconds} ms)");
                _stopwatches.Remove(name);
            }
            else
            {
                LogWithMemory($"{name} END (Duration: unknown)");
            }
        }
    }

    public static void StartHeartbeat(System.Windows.Threading.Dispatcher dispatcher)
    {
        dispatcher.Invoke(() =>
        {
            var timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromSeconds(1),
                System.Windows.Threading.DispatcherPriority.Normal,
                (s, e) => Log("UI_HEARTBEAT"),
                dispatcher);
            timer.Start();
            Log("UI HEARTBEAT Started.");
        });
    }
}
