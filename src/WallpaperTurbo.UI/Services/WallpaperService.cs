using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WallpaperTurbo.UI.Services;

public class WallpaperEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    public string Video { get; set; } = string.Empty;

    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    // Derived visual helpers
    public string Resolution => Id.Contains("frieren") || Id.Contains("crimson") ? "3840 x 2160" : "3440 x 1440";
    public string Fps => Id.Contains("frieren") || Id.Contains("crimson") ? "60 FPS" : "30 FPS";
    public string TagsDisplay => string.Join(" • ", Tags).ToUpperInvariant();
}

public class WallpaperManifest
{
    [JsonPropertyName("wallpapers")]
    public List<WallpaperEntry> Wallpapers { get; set; } = new();
}

public class WallpaperService
{
    private readonly string _manifestPath;
    private readonly string _appRunnerDir;
    private readonly string _appRunnerExePath;
    private List<WallpaperEntry> _wallpapers = new();
    private int _activeWallpaperIndex = -1;

    public string ActivePauseProfile { get; set; } = "Maximized";
    public bool UseSoftwareDecoding { get; set; } = false;

    public WallpaperService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // Resolve path to WallpaperTurbo.AppRunner
        // Check local publish/debug first, fallback to visual source structure
        string appRunnerCandidate = Path.Combine(baseDir, "WallpaperTurbo.AppRunner.exe");
        if (File.Exists(appRunnerCandidate))
        {
            _appRunnerExePath = appRunnerCandidate;
            _appRunnerDir = baseDir;
        }
        else
        {
            // Back out from UI debug directory to solution structure:
            // "src/WallpaperTurbo.UI/bin/Debug/net8.0-windows" -> 4 directories up to src/
            string srcPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            
            string dir1 = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows");
            string dir2 = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Debug", "net8.0-windows");
            string dir3 = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows", "win-x64");
            
            if (File.Exists(Path.Combine(dir1, "WallpaperTurbo.AppRunner.exe")))
            {
                _appRunnerDir = dir1;
            }
            else if (File.Exists(Path.Combine(dir2, "WallpaperTurbo.AppRunner.exe")))
            {
                _appRunnerDir = dir2;
            }
            else
            {
                _appRunnerDir = dir3; // Fallback
            }
            
            _appRunnerExePath = Path.Combine(_appRunnerDir, "WallpaperTurbo.AppRunner.exe");
        }

        _manifestPath = Path.Combine(_appRunnerDir, "Assets", "WallpaperManifest.json");
    }

    public async Task<List<WallpaperEntry>> GetWallpapersAsync()
    {
        if (_wallpapers.Any())
            return _wallpapers;

        try
        {
            if (!File.Exists(_manifestPath))
            {
                Debug.WriteLine($"Manifest not found at: {_manifestPath}");
                return new List<WallpaperEntry>();
            }

            string json = await File.ReadAllTextAsync(_manifestPath);
            var manifest = JsonSerializer.Deserialize<WallpaperManifest>(json);
            if (manifest != null)
            {
                _wallpapers = manifest.Wallpapers;
                // Try to resolve relative thumbnail paths to absolute paths
                foreach (var wp in _wallpapers)
                {
                    wp.Thumbnail = Path.Combine(_appRunnerDir, wp.Thumbnail);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading manifest: {ex.Message}");
        }

        return _wallpapers;
    }

    public bool IsEngineRunning()
    {
        // Check for AppRunner process name
        var runnerProcesses = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
        if (runnerProcesses.Any())
            return true;

        // Also check if running via dotnet exec
        var dotnetProcesses = Process.GetProcessesByName("dotnet");
        foreach (var p in dotnetProcesses)
        {
            try
            {
                foreach (ProcessModule m in p.Modules)
                {
                    if (m.ModuleName.Contains("WallpaperTurbo.AppRunner", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Protect against architectural mismatches or access-denied
            }
        }

        return false;
    }

    public async Task<bool> LaunchWallpaperAsync(int index, string? pauseMode = null, bool? softwareDecode = null)
    {
        if (!File.Exists(_appRunnerExePath))
        {
            Debug.WriteLine($"AppRunner executable not found at: {_appRunnerExePath}");
            return false;
        }

        _activeWallpaperIndex = index;

        string mode = pauseMode ?? ActivePauseProfile;
        bool softDecode = softwareDecode ?? UseSoftwareDecoding;

        // Map UI "Disabled" option to AppRunner "None" parameter
        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            mode = "None";
        }

        return await Task.Run(() =>
        {
            try
            {
                string decodeArg = softDecode ? " --software-decode" : string.Empty;
                string args = $"--detach --wallpaper {index} --silent --pause-mode {mode}{decodeArg}";

                var psi = new ProcessStartInfo
                {
                    FileName = _appRunnerExePath,
                    Arguments = args,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = _appRunnerDir
                };

                using var p = Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching wallpaper process: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> StopPlaybackAsync()
    {
        if (!File.Exists(_appRunnerExePath))
            return false;

        _activeWallpaperIndex = -1;

        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _appRunnerExePath,
                    Arguments = "--stop",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = _appRunnerDir
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping wallpaper playback: {ex.Message}");
                return false;
            }
        });
    }

    public int GetActiveWallpaperIndex() => _activeWallpaperIndex;
}
