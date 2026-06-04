using System;
using System.IO;
using System.Text;

namespace WallpaperTurbo.Updater;

/// <summary>
/// Temporary diagnostic logger for update detection failure investigation.
/// Writes timestamped entries to %LOCALAPPDATA%\WallpaperTurbo\updater_diagnostic.log.
/// File is truncated at startup of each new process so every run produces a clean log.
/// </summary>
public static class UpdaterDiagnostic
{
    private static readonly object _lock = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperTurbo",
        "updater_diagnostic.log");

    public static void Init()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Truncate on first write of a new process
            lock (_lock)
            {
                File.WriteAllText(LogPath, $"=== Diagnostic run started at {DateTime.Now:O} ==={Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostic must never break the real pipeline.
        }
    }

    public static void Log(string layer, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{layer}] {message}";
        try
        {
            System.Diagnostics.Debug.WriteLine(line);
        }
        catch { }
        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostic must never break the real pipeline.
        }
    }
}
