using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WallpaperTurbo.Core.Services.Performance;

/// <summary>
/// A thread-safe and cross-process safe logger for memory statistics.
/// </summary>
public static class MemoryLogger
{
    private static readonly string LogFilePath;
    private static readonly object _lock = new object();
    private static bool _headerWritten = false;

    static MemoryLogger()
    {
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        LogFilePath = Path.Combine(logDir, "memory_usage.csv");
        EnsureHeader();
    }

    private static void EnsureHeader()
    {
        try
        {
            if (!File.Exists(LogFilePath))
            {
                WriteToFile("Timestamp,Process,PrivateMemory_MB,WorkingSet_MB,ManagedMemory_MB,GCHeap_MB,Handles,Threads\n");
            }
            _headerWritten = true;
        }
        catch
        {
            // Ignore if we can't write the header right now
        }
    }

    public static void LogMemoryStats(string processName)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();
            double privateMemoryMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);
            double workingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);
            double managedMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            double gcHeapMb = gcInfo.HeapSizeBytes / (1024.0 * 1024.0);

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{processName}," +
                          $"{privateMemoryMb:F1},{workingSetMb:F1},{managedMemoryMb:F1},{gcHeapMb:F1}," +
                          $"{process.HandleCount},{process.Threads.Count}\n";

            WriteToFile(line);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoryLogger] Failed to log stats: {ex.Message}");
        }
    }

    private static void WriteToFile(string content)
    {
        lock (_lock)
        {
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using (var fs = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(fs))
                    {
                        writer.Write(content);
                    }
                    break;
                }
                catch (IOException)
                {
                    retries--;
                    Thread.Sleep(50);
                }
            }
        }
    }
}
