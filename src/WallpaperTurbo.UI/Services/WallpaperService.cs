using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Services;

public class WallpaperEntry : ObservableObject
{
    private string _id = string.Empty;
    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    private string _title = string.Empty;
    [JsonPropertyName("title")]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _video = string.Empty;
    [JsonPropertyName("video")]
    public string Video
    {
        get => _video;
        set => SetProperty(ref _video, value);
    }

    private string _thumbnail = string.Empty;
    [JsonPropertyName("thumbnail")]
    public string Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                // When path changes, load BitmapImage asynchronously on a background thread.
                // This eliminates all synchronous JPEG decoding from the UI dispatcher.
                _ = LoadThumbnailInternalAsync(value);
            }
        }
    }

    // ── Async-loaded frozen bitmap, safe to bind directly without any converter ──
    private ImageSource? _loadedThumbnail;
    [JsonIgnore]
    public ImageSource? LoadedThumbnail
    {
        get => _loadedThumbnail;
        private set
        {
            // Track bitmap lifecycle for VRAM diagnostics
            if (_loadedThumbnail != null && !(value == null))
                DiagnosticsService.OnBitmapEvicted(); // replacing existing
            else if (_loadedThumbnail == null && value != null)
                DiagnosticsService.OnBitmapLoaded();
            else if (_loadedThumbnail != null && value == null)
                DiagnosticsService.OnBitmapEvicted();
            SetProperty(ref _loadedThumbnail, value);
        }
    }

    /// <summary>
    /// Explicitly clear the loaded bitmap to allow GC/VRAM reclamation.
    /// Called by VirtualizingWrapPanel when an item scrolls far outside the cache zone.
    /// The bitmap will be reloaded lazily when the item scrolls back into view.
    /// </summary>
    public void EvictThumbnail()
    {
        if (DebugFlags.SafeDebugMode && !DebugFlags.EnableThumbnailEviction)
        {
            Debug.WriteLine("[ISOLATE] EvictThumbnail requested but bypassed via EnableThumbnailEviction = false.");
            return;
        }

        if (_loadedThumbnail != null)
        {
            // Reset to null → WPF Image shows nothing → GC can collect the BitmapImage
            // Next time Thumbnail is set (or on next scroll-in), it reloads from disk.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                LoadedThumbnail = null;
            else
                dispatcher.BeginInvoke(() => LoadedThumbnail = null, DispatcherPriority.Background);
        }
    }

    // Static fallback: created once, frozen, reused for all entries
    private static BitmapImage? _fallbackBitmap;
    private static readonly object _fallbackLock = new();

    private static BitmapImage EnsureFallback()
    {
        lock (_fallbackLock)
        {
            if (_fallbackBitmap == null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/Assets/Branding/wallpaper-turbo.ico", UriKind.Absolute);
                bmp.DecodePixelWidth = 320;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _fallbackBitmap = bmp;
            }
            return _fallbackBitmap;
        }
    }

    private async Task LoadThumbnailInternalAsync(string path)
    {
        // Pack URIs and empty paths → show no image immediately (no disk I/O)
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyThumbnailToUI(null);
            return;
        }

        if (DebugFlags.SafeDebugMode && !DebugFlags.EnableAsyncThumbnailLoading)
        {
            Debug.WriteLine($"[ISOLATE] LoadThumbnailInternalAsync (SYNC on UI thread) for: {path}");
            ImageSource? result = null;
            try
            {
                if (File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.DecodePixelWidth = 320;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    result = bmp;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ISOLATE] Sync load error: {ex.Message}");
            }
            LoadedThumbnail = result;
            return;
        }

        // Track decode queue depth for diagnostics
        DiagnosticsService.OnDecodeQueued();
        try
        {
            // Real file paths: decode on threadpool, freeze, dispatch result to UI
            ImageSource? result = await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.DecodePixelWidth = 320; // Pre-scale to card width; saves VRAM
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze(); // Must freeze before crossing thread boundary
                    return (ImageSource?)bmp;
                }
                catch
                {
                    return null;
                }
            });

            await ApplyThumbnailToUI(result);
        }
        finally
        {
            DiagnosticsService.OnDecodeCompleted();
        }
    }

    private async Task ApplyThumbnailToUI(ImageSource? source)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            LoadedThumbnail = source;
        }
        else
        {
            await dispatcher.InvokeAsync(
                () => LoadedThumbnail = source,
                DispatcherPriority.Background);
        }
    }

    private string _author = string.Empty;
    [JsonPropertyName("author")]
    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    private List<string> _tags = new();
    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }

    private bool _isActive;
    [JsonIgnore]
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    private System.Windows.Media.ImageSource? _previewSource;
    [JsonIgnore]
    public System.Windows.Media.ImageSource? PreviewSource
    {
        get => _previewSource;
        set => SetProperty(ref _previewSource, value);
    }

    private bool _isPreviewActive;
    [JsonIgnore]
    public bool IsPreviewActive
    {
        get => _isPreviewActive;
        set => SetProperty(ref _isPreviewActive, value);
    }

    private bool _isFallbackThumbnail;
    [JsonIgnore]
    public bool IsFallbackThumbnail
    {
        get => _isFallbackThumbnail;
        set => SetProperty(ref _isFallbackThumbnail, value);
    }

    // Derived visual helpers
    public string Resolution => Id.Contains("frieren") || Id.Contains("crimson") ? "3840 x 2160" : "3440 x 1440";
    public string Fps => Id.Contains("frieren") || Id.Contains("crimson") ? "60 FPS" : "30 FPS";
    public string TagsDisplay => string.Join(" • ", Tags).ToUpperInvariant();
    [JsonIgnore]
    public bool IsDeletable => true;
}

public class WallpaperManifest
{
    [JsonPropertyName("wallpapers")]
    public List<WallpaperEntry> Wallpapers { get; set; } = new();
}

public class WallpaperService
{
    private readonly IWallpaperLibraryService _libraryService;
    private readonly string _manifestPath;
    private readonly string _appRunnerDir;
    private readonly string _appRunnerExePath;
    private List<WallpaperEntry> _wallpapers = new();
    private int _activeWallpaperIndex = -1;
    private bool _mockEngineRunning = false; // Mock engine status for SafeDebugMode

    public string ActivePauseProfile { get; set; } = "Maximized";
    public bool UseSoftwareDecoding { get; set; } = false;

    public WallpaperService(IWallpaperLibraryService libraryService)
    {
        _libraryService = libraryService;
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
        var list = await _libraryService.GetWallpapersAsync();
        
        // Merge list with _wallpapers in-place to preserve original WallpaperEntry instances
        var mergedList = new List<WallpaperEntry>();
        foreach (var incoming in list)
        {
            var existing = _wallpapers.FirstOrDefault(w => w.Id == incoming.Id);
            if (existing != null)
            {
                // In-place update to preserve the reference (and LoadedThumbnail!)
                existing.Title = incoming.Title;
                existing.Video = incoming.Video;
                existing.Author = incoming.Author;
                existing.Tags = incoming.Tags;
                existing.IsFallbackThumbnail = incoming.IsFallbackThumbnail;
                
                // Only update Thumbnail path if it has actually changed, to avoid re-triggering disk I/O
                if (existing.Thumbnail != incoming.Thumbnail)
                {
                    existing.Thumbnail = incoming.Thumbnail;
                }
                mergedList.Add(existing);
            }
            else
            {
                mergedList.Add(incoming);
            }
        }
        
        _wallpapers = mergedList;
        
        // Sync active states on reload
        bool running = IsEngineRunning();
        UpdateActiveStates(running ? _activeWallpaperIndex : -1);
        
        return _wallpapers;
    }

    private void UpdateActiveStates(int activeIndex)
    {
        for (int i = 0; i < _wallpapers.Count; i++)
        {
            _wallpapers[i].IsActive = (i == activeIndex - 1);
        }
    }

    public bool IsEngineRunning()
    {
        if (DebugFlags.SafeDebugMode)
        {
            return _mockEngineRunning;
        }

        bool isRunning = false;

        // Fast path: check for named AppRunner process - this is O(1) and non-blocking
        var runnerProcesses = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
        if (runnerProcesses.Any())
        {
            isRunning = true;
        }
        // NOTE: Removed dotnet module enumeration fallback - iterating p.Modules is a blocking 
        // Win32 syscall that can stall for 100-300ms per process, causing UI freezes when 
        // called on the telemetry timer callback. Named process check is sufficient.

        if (isRunning)
        {
            SyncActiveStateFromFile();
        }
        else if (_activeWallpaperIndex != -1)
        {
            _activeWallpaperIndex = -1;
            UpdateActiveStates(-1);
        }

        return isRunning;
    }

    private void SyncActiveStateFromFile()
    {
        try
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperTurbo");
            string stateFilePath = Path.Combine(appDataDir, "active_state.json");
            if (File.Exists(stateFilePath))
            {
                using var fs = new FileStream(stateFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                if (root.TryGetProperty("ActiveWallpaperIndex", out var idxProp))
                {
                    int index = idxProp.GetInt32();
                    if (index != _activeWallpaperIndex)
                    {
                        _activeWallpaperIndex = index;
                        UpdateActiveStates(index);
                        
                        if (index > 0 && index <= _wallpapers.Count)
                        {
                            var wp = _wallpapers[index - 1];
                            var mainVm = App.GetService<MainViewModel>();
                            if (mainVm != null)
                            {
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    mainVm.SetActiveWallpaperInfo(wp.Title, $"{wp.Resolution} • {wp.Fps}");
                                }));
                            }
                        }
                    }
                }
                
                if (root.TryGetProperty("ActiveWallpaperTitle", out var titleProp))
                {
                    string title = titleProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(title))
                    {
                        var mainVm = App.GetService<MainViewModel>();
                        if (mainVm != null && mainVm.ActiveWallpaperTitle != title)
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                var wp = _wallpapers.FirstOrDefault(w => w.Title == title);
                                string specs = wp != null ? $"{wp.Resolution} • {wp.Fps}" : "3840 x 2160 • 60 FPS";
                                mainVm.SetActiveWallpaperInfo(title, specs);
                            }));
                        }
                    }
                }

                if (root.TryGetProperty("IsPlaying", out var playingProp))
                {
                    bool isPlaying = playingProp.GetBoolean();
                    var mainVm = App.GetService<MainViewModel>();
                    if (mainVm != null && mainVm.IsPlaying != isPlaying)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            mainVm.IsPlaying = isPlaying;
                        }));
                    }
                }
            }
        }
        catch
        {
            // Ignore file read/parse errors during polling
        }
    }

    public async Task<bool> LaunchWallpaperAsync(int index, string? pauseMode = null, bool? softwareDecode = null, bool forceFreshLaunch = false)
    {
        DiagnosticsService.SetAction($"Wallpaper Service Launching Wallpaper: Index {index} (ForceFresh: {forceFreshLaunch})");

        if (DebugFlags.SafeDebugMode)
        {
            Debug.WriteLine($"[ISOLATE] LaunchWallpaperAsync requested for index: {index}, pauseMode: {pauseMode}, softwareDecode: {softwareDecode}, forceFreshLaunch: {forceFreshLaunch}");
            _activeWallpaperIndex = index;
            UpdateActiveStates(index);
            _mockEngineRunning = true;
            DiagnosticsService.SetAction("Wallpaper Service Idle / Launch complete (SafeDebugMode)");
            return await Task.FromResult(true);
        }

        // Try to swap in real-time over IPC Named Pipe first (skip if fresh launch is forced)
        if (!forceFreshLaunch && await SendIpcCommandAsync($"swap {index}"))
        {
            _activeWallpaperIndex = index;
            UpdateActiveStates(index);
            DiagnosticsService.SetAction("Wallpaper Service Idle / Swap via IPC complete");
            return true;
        }

        if (!File.Exists(_appRunnerExePath))
        {
            Debug.WriteLine($"AppRunner executable not found at: {_appRunnerExePath}");
            DiagnosticsService.SetAction("Wallpaper Service Idle / Launch failed (Exe missing)");
            return false;
        }

        _activeWallpaperIndex = index;
        UpdateActiveStates(index);

        string mode = pauseMode ?? ActivePauseProfile;
        bool softDecode = softwareDecode ?? UseSoftwareDecoding;

        // Map UI "Disabled" option to AppRunner "None" parameter
        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            mode = "None";
        }

        bool result = await Task.Run(() =>
        {
            try
            {
                string decodeArg = softDecode ? " --software-decode" : string.Empty;
                int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                string args = $"--detach --wallpaper {index} --silent --pause-mode {mode}{decodeArg} --ui-pid {currentPid}";

                DiagnosticsService.SetAction($"Wallpaper Service Starting process: {args}");

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

        DiagnosticsService.SetAction("Wallpaper Service Idle / Launch complete");
        return result;
    }

    public async Task<bool> StopPlaybackAsync()
    {
        DiagnosticsService.SetAction("Wallpaper Service Stopping Playback");

        if (DebugFlags.SafeDebugMode)
        {
            Debug.WriteLine("[ISOLATE] StopPlaybackAsync requested.");
            _activeWallpaperIndex = -1;
            UpdateActiveStates(-1);
            _mockEngineRunning = false;
            DiagnosticsService.SetAction("Wallpaper Service Idle / Stop complete (SafeDebugMode)");
            return await Task.FromResult(true);
        }

        if (!File.Exists(_appRunnerExePath))
        {
            DiagnosticsService.SetAction("Wallpaper Service Idle / Stop failed (Exe missing)");
            return false;
        }

        _activeWallpaperIndex = -1;
        UpdateActiveStates(-1);

        bool result = await Task.Run(() =>
        {
            try
            {
                DiagnosticsService.SetAction("Wallpaper Service Starting stop process");

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
                DiagnosticsService.SetAction("Wallpaper Service Waiting for stop process exit");
                p?.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping wallpaper playback: {ex.Message}");
                return false;
            }
        });

        DiagnosticsService.SetAction("Wallpaper Service Idle / Stop complete");
        return result;
    }

    public int GetActiveWallpaperIndex() => _activeWallpaperIndex;

    public async Task<bool> DeleteWallpaperAsync(WallpaperEntry wp)
    {
        // 1. If currently playing, stop playback first
        int index = _wallpapers.IndexOf(wp);
        if (wp.IsActive || _activeWallpaperIndex == index + 1)
        {
            await StopPlaybackAsync();
        }

        // 2. Call library service to delete manifest entry & disk folder
        bool success = await _libraryService.DeleteWallpaperAsync(wp.Id);
        if (success)
        {
            // Remove from the local cache list
            _wallpapers.Remove(wp);
        }
        return success;
    }

    public async Task<bool> PausePlaybackAsync()
    {
        DiagnosticsService.SetAction("Wallpaper Service Pausing Playback via IPC");
        return await SendIpcCommandAsync("pause");
    }

    public async Task<bool> ResumePlaybackAsync()
    {
        DiagnosticsService.SetAction("Wallpaper Service Resuming Playback via IPC");
        return await SendIpcCommandAsync("play");
    }

    private async Task<bool> SendIpcCommandAsync(string command)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", "WallpaperTurbo_IPC", System.IO.Pipes.PipeDirection.Out);
            await client.ConnectAsync(150); // 150ms timeout for instant responsiveness
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(command);
            return true;
        }
        catch
        {
            return false; // Engine not running or IPC not responsive
        }
    }
}

