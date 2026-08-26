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
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.UI.Models;
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

    private string _resolution = string.Empty;
    [JsonPropertyName("resolution")]
    public string Resolution
    {
        get => string.IsNullOrEmpty(_resolution) ? "1920 x 1080" : _resolution;
        set => SetProperty(ref _resolution, value);
    }

    private string _fps = string.Empty;
    [JsonPropertyName("fps")]
    public string Fps
    {
        get => string.IsNullOrEmpty(_fps) ? "30 FPS" : _fps;
        set
        {
            if (value != null)
            {
                value = value.Replace(" FPS", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" fps", "", StringComparison.OrdinalIgnoreCase)
                             .Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    value = value + " FPS";
                }
            }
            SetProperty(ref _fps, value ?? string.Empty);
        }
    }

    private bool _isUserImported;
    [JsonPropertyName("isUserImported")]
    public bool IsUserImported
    {
        get => _isUserImported;
        set => SetProperty(ref _isUserImported, value);
    }

    public string TagsDisplay => string.Join(" • ", Tags).ToUpperInvariant();
    [JsonIgnore]
    public bool IsDeletable => IsUserImported;
}

public class WallpaperManifest
{
    [JsonPropertyName("wallpapers")]
    public List<WallpaperEntry> Wallpapers { get; set; } = new();
}

public class WallpaperSessionEventArgs : EventArgs
{
    public string WallpaperTitle { get; }
    public string ThumbnailPath { get; }
    public bool IsPlaying { get; }
    public bool IsActive { get; }

    public WallpaperSessionEventArgs(string title, string thumbnailPath, bool isPlaying, bool isActive)
    {
        WallpaperTitle = title;
        ThumbnailPath = thumbnailPath;
        IsPlaying = isPlaying;
        IsActive = isActive;
    }
}

public class WallpaperService
    {
        private readonly IWallpaperLibraryService _libraryService;
        private readonly ISettingsStore _settingsStore;
        private readonly IGpuPreferenceService _gpuPreferenceService;
        private string _manifestPath = string.Empty;
        private string _appRunnerDir = string.Empty;
        private string _appRunnerExePath = string.Empty; // Non-readonly so test fixtures can override via reflection
        private List<WallpaperEntry> _wallpapers = new();
        private readonly object _wallpaperLock = new();
        private int _activeWallpaperIndex = -1;
        private int _lastActiveWallpaperIndex = -1;
        private string? _activeWallpaperId; // Track by ID for stability across reloads
        private bool _mockEngineRunning = false; // Mock engine status for SafeDebugMode
        private DateTime _lastStateFileWriteTime = DateTime.MinValue;
        private string? _cachedIpcPipeName;
        private readonly SemaphoreSlim _launchGate = new(1, 1); // Single-flight gate for wallpaper launches
        private int _launchGeneration; // Increments on each launch request to discard stale completions
        private DateTime _lastProcessCheck = DateTime.MinValue;
        private bool _lastIsRunning = false;
        private readonly object _engineProbeLock = new();

        /// <summary>How long a process-table scan result stays valid.</summary>
        private static readonly TimeSpan ProcessProbeTtl = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// The raw "is an AppRunner process alive?" probe. Overridable so tests do not depend on
        /// whether a real AppRunner happens to be running on the machine — on a dev box it usually
        /// is. Production always uses the process-table scan below.
        /// </summary>
        internal Func<bool> AppRunnerProcessProbe { get; set; } = static () =>
        {
            // Fast path: check for named AppRunner process - this is O(1) and non-blocking
            var runnerProcesses = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
            bool alive = runnerProcesses.Length > 0;

            foreach (var p in runnerProcesses)
            {
                p.Dispose();
            }

            return alive;
        };

        public WallpaperSessionEventArgs? ActiveSession { get; private set; }
        public event EventHandler<WallpaperSessionEventArgs>? SessionStateChanged;

        /// <summary>
        /// Raised immediately before an intentional engine restart (e.g. a GPU-preference
        /// switch). Lets observers such as the visibility watchdog pause loss-detection so a
        /// temporary window disappearance during the restart is not mistaken for a crash and
        /// relaunched mid-restart (which collides with the restart itself).
        /// </summary>
        public event EventHandler? EngineRestarting;

        /// <summary>
        /// Raised when an intentional engine restart finishes (whether it succeeded or not).
        /// </summary>
        public event EventHandler? EngineRestartCompleted;

        public int LastActiveWallpaperIndex => _lastActiveWallpaperIndex;

        /// <summary>
        /// Records the (1-based) index playback should return to, without starting it.
        /// </summary>
        /// <remarks>
        /// <see cref="_lastActiveWallpaperIndex"/> is otherwise written only by
        /// <see cref="StopPlaybackAsync"/>, which captures whatever was already playing. When
        /// startup declines to launch at all (battery saver), nothing ever plays, so that field
        /// stays -1 and a later resume has no target to relaunch. This lets the startup path
        /// hand over the index it resolved but chose not to use. Ignores negative values so a
        /// failed resolution cannot erase a real target.
        /// </remarks>
        public void SetDeferredWallpaperIndex(int index)
        {
            if (index < 0)
            {
                return;
            }

            lock (_wallpaperLock)
            {
                _lastActiveWallpaperIndex = index;
            }
        }

        private string _activePauseProfile = "Maximized";
        public string ActivePauseProfile
        {
            get => _activePauseProfile;
            set
            {
                if (_activePauseProfile != value)
                {
                    _activePauseProfile = value;
                    _ = UpdatePauseProfileAsync(value);
                }
            }
        }
        public bool UseSoftwareDecoding { get; set; } = false;
        public string AppRunnerExePath => _appRunnerExePath;
        public int ActiveWallpaperIndex => _activeWallpaperIndex;

    public WallpaperService(IWallpaperLibraryService libraryService, ISettingsStore settingsStore, IGpuPreferenceService gpuPreferenceService)
    {
        _libraryService = libraryService;
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _gpuPreferenceService = gpuPreferenceService ?? throw new ArgumentNullException(nameof(gpuPreferenceService));
        
        // Initialize active pause profile from persisted settings
        var settings = _settingsStore.Load();
        _activePauseProfile = settings.PauseOnMaximized ? "Maximized" : "Disabled";
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
            
            // Search all common build output directories (both Debug and Release configurations across various architectures)
            var candidates = new[]
            {
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Debug", "net8.0-windows"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows", "win-x64"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Debug", "net8.0-windows", "win-x64"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Release", "net8.0-windows"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Release", "net8.0-windows"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Release", "net8.0-windows", "win-x64"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Release", "net8.0-windows", "win-x64")
            };

            string fallbackDir = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows", "win-x64");
            _appRunnerDir = candidates.FirstOrDefault(dir => File.Exists(Path.Combine(dir, "WallpaperTurbo.AppRunner.exe"))) ?? fallbackDir;
            _appRunnerExePath = Path.Combine(_appRunnerDir, "WallpaperTurbo.AppRunner.exe");
        }

        _manifestPath = Path.Combine(_appRunnerDir, "Assets", "WallpaperManifest.json");

        // GPU preference registry sync is performed in LaunchWallpaperAsync (on a background
        // thread, immediately before Process.Start) rather than here in the constructor.
        // This avoids blocking the DI startup thread and prevents race conditions with
        // the UI's own apply path.
    }

    /// <summary>
    /// Testable constructor that accepts an explicit AppRunner exe path,
    /// bypassing the filesystem probe. For use in unit/integration tests only.
    /// </summary>
    internal WallpaperService(IWallpaperLibraryService libraryService, ISettingsStore settingsStore, IGpuPreferenceService gpuPreferenceService, string appRunnerExePath)
        : this(libraryService, settingsStore, gpuPreferenceService)
    {
        // Override the probed path with the caller-supplied test path.
        _appRunnerExePath = appRunnerExePath;
        _appRunnerDir = Path.GetDirectoryName(appRunnerExePath) ?? string.Empty;
        _manifestPath = Path.Combine(_appRunnerDir, "Assets", "WallpaperManifest.json");
    }

    public async Task<List<WallpaperEntry>> GetWallpapersAsync()
    {
        var list = await _libraryService.GetWallpapersAsync();
        lock (_wallpaperLock)
        {
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

            return _wallpapers.ToList();
        }
    }

    private void UpdateActiveStates(int activeIndex)
    {
        lock (_wallpaperLock)
        {
            for (int i = 0; i < _wallpapers.Count; i++)
            {
                _wallpapers[i].IsActive = (i == activeIndex - 1);
            }
        }
    }

    /// <summary>
    /// Caches only the expensive part: the process-table scan. Held under a lock because callers
    /// span the UI thread, the visibility watchdog's 1s poll and the SystemEvents thread.
    /// </summary>
    private bool IsAppRunnerProcessAlive()
    {
        lock (_engineProbeLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastProcessCheck < ProcessProbeTtl)
            {
                return _lastIsRunning;
            }

            bool alive = AppRunnerProcessProbe();

            _lastProcessCheck = now;
            _lastIsRunning = alive;
            return alive;
        }
    }

    /// <summary>
    /// Returns whether the engine is running, and reconciles observable state with that answer.
    /// </summary>
    /// <remarks>
    /// This is not a pure query. It owns <see cref="SyncActiveStateFromFile"/>, the
    /// <c>_activeWallpaperIndex</c> reset and the only engine-died <see cref="SessionStateChanged"/>
    /// publish, so the reconciliation below must run on <i>every</i> call. Only the process probe is
    /// cached (see <see cref="IsAppRunnerProcessAlive"/>); short-circuiting the whole method left
    /// callers such as <c>ReloadWallpapers</c> reading a stale active index.
    /// <c>SyncActiveStateFromFile</c> is itself cheap on repeat calls — it early-exits on the
    /// state file's last-write timestamp.
    /// </remarks>
    public bool IsEngineRunning()
    {
        if (DebugFlags.SafeDebugMode)
        {
            return _mockEngineRunning;
        }

        bool isRunning = IsAppRunnerProcessAlive();

        if (isRunning)
        {
            SyncActiveStateFromFile();
        }
        else
        {
            if (_activeWallpaperIndex != -1)
            {
                _activeWallpaperIndex = -1;
                UpdateActiveStates(-1);
            }
            _lastStateFileWriteTime = DateTime.MinValue;
            var newSession = new WallpaperSessionEventArgs("", "", false, false);
            if (ActiveSession == null || ActiveSession.IsActive)
            {
                ActiveSession = newSession;
                SessionStateChanged?.Invoke(this, newSession);
            }
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
                    // Performance cache optimization: Check last write time before parsing JSON from disk
                    DateTime currentWriteTime = File.GetLastWriteTimeUtc(stateFilePath);
                    if (currentWriteTime == _lastStateFileWriteTime)
                    {
                        return;
                    }
                    _lastStateFileWriteTime = currentWriteTime;

                    string title = string.Empty;
                    bool isPlaying = false;
                    int activeIndex = _activeWallpaperIndex;

                    using var fs = new FileStream(stateFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var doc = JsonDocument.Parse(fs);
                    var root = doc.RootElement;

                    // Validate state is not stale: must have ProcessId matching a running AppRunner, and recent UpdatedAtUtc
                    if (!ValidateStateFile(root))
                    {
                        // Stale state — clear cached pipe name so we don't retry a dead IPC
                        ClearCachedIpcPipeName();
                        return;
                    }

                    if (root.TryGetProperty("ActiveWallpaperIndex", out var idxProp))
                    {
                        int index = idxProp.GetInt32();
                        activeIndex = index;
                        if (index != _activeWallpaperIndex)
                        {
                            _activeWallpaperIndex = index;
                            UpdateActiveStates(index);
                                
                            WallpaperEntry? activeWallpaper = null;
                            lock (_wallpaperLock)
                            {
                                if (index > 0 && index <= _wallpapers.Count)
                                {
                                    activeWallpaper = _wallpapers[index - 1];
                                }
                            }

                            if (activeWallpaper != null)
                            {
                                _activeWallpaperId = activeWallpaper.Id;
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    var mainVm = App.GetService<MainViewModel>();
                                    if (mainVm != null)
                                    {
                                        mainVm.SetActiveWallpaperInfo(activeWallpaper.Title, $"{activeWallpaper.Resolution} • {activeWallpaper.Fps}");
                                    }
                                }));
                            }
                        }
                    }
                    
                    if (root.TryGetProperty("ActiveWallpaperTitle", out var titleProp))
                    {
                        title = titleProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(title))
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                var mainVm = App.GetService<MainViewModel>();
                                if (mainVm != null && mainVm.ActiveWallpaperTitle != title)
                                {
                                    WallpaperEntry? wp;
                                    lock (_wallpaperLock)
                                    {
                                        wp = _wallpapers.FirstOrDefault(w => w.Title == title);
                                    }
                                    string specs = wp != null ? $"{wp.Resolution} • {wp.Fps}" : "3840 x 2160 • 60 FPS";
                                    mainVm.SetActiveWallpaperInfo(title, specs);
                                }
                            }));
                        }
                    }

                    if (root.TryGetProperty("IsPlaying", out var playingProp))
                    {
                        isPlaying = playingProp.GetBoolean();
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            var mainVm = App.GetService<MainViewModel>();
                            if (mainVm != null && mainVm.IsPlaying != isPlaying)
                            {
                                mainVm.IsPlaying = isPlaying;
                            }
                        }));
                    }

                    WallpaperEntry? activeWallpaperForSession = null;
                    lock (_wallpaperLock)
                    {
                        if (activeIndex > 0 && activeIndex <= _wallpapers.Count)
                        {
                            activeWallpaperForSession = _wallpapers[activeIndex - 1];
                            if (string.IsNullOrEmpty(title))
                            {
                                title = activeWallpaperForSession.Title;
                            }
                        }
                    }

                    // Notify session changed
                    bool isVisible = isPlaying && (activeIndex > 0);
                    string thumbnail = "";
                    if (activeWallpaperForSession != null)
                    {
                        thumbnail = activeWallpaperForSession.Thumbnail;
                    }
                    
                    var newSession = new WallpaperSessionEventArgs(title, thumbnail, isPlaying, isVisible);
                    if (ActiveSession == null || 
                        ActiveSession.WallpaperTitle != newSession.WallpaperTitle || 
                        ActiveSession.IsPlaying != newSession.IsPlaying || 
                        ActiveSession.IsActive != newSession.IsActive)
                    {
                        ActiveSession = newSession;
                        SessionStateChanged?.Invoke(this, newSession);
                    }
                }
            }
            catch
            {
                // Ignore file read/parse errors during polling
            }
        }

        private bool ValidateStateFile(JsonElement root)
        {
            try
            {
                // Must have ProcessId matching a running AppRunner
                if (!root.TryGetProperty("ProcessId", out var pidProp))
                {
                    Debug.WriteLine("[WallpaperService] State file missing ProcessId — stale");
                    return false;
                }
                int statePid = pidProp.GetInt32();
                var runnerProcesses = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
                bool pidMatches = runnerProcesses.Any(p => p.Id == statePid);
                foreach (var p in runnerProcesses) p.Dispose();
                if (!pidMatches)
                {
                    Debug.WriteLine($"[WallpaperService] State PID {statePid} not running — stale");
                    return false;
                }

                // Must have recent UpdatedAtUtc (within 10 minutes - heartbeat is 60s, window extended to avoid stale clears during brief hiccups)
                if (root.TryGetProperty("UpdatedAtUtc", out var updatedProp))
                {
                    if (DateTime.TryParse(updatedProp.GetString(), out var updated))
                    {
                        if (DateTime.UtcNow - updated > TimeSpan.FromMinutes(10))
                        {
                            Debug.WriteLine($"[WallpaperService] State file older than 10 minutes ({updated}) — stale");
                            return false;
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("[WallpaperService] State file missing UpdatedAtUtc — stale format");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WallpaperService] State validation failed: {ex.Message}");
                return false;
            }
        }

        public void ClearCachedIpcPipeName()
        {
            _cachedIpcPipeName = null;
        }

        public async Task<bool> PingIpcAsync()
        {
            try
            {
                var result = await SendIpcCommandAsync("ping");
                return result == "pong";
            }
            catch
            {
                return false;
            }
        }

public async Task<bool> LaunchWallpaperAsync(int index, string? pauseMode = null, bool? softwareDecode = null, bool forceFreshLaunch = false)
        {
            DiagnosticsService.SetAction($"Wallpaper Service Launching Wallpaper: Index {index} (ForceFresh: {forceFreshLaunch})");

            if (DebugFlags.SafeDebugMode)
            {
                Debug.WriteLine($"[ISOLATE] LaunchWallpaperAsync requested for index: {index}, pauseMode: {pauseMode}, softwareDecode: {softwareDecode}, forceFreshLaunch: {forceFreshLaunch}");
                string title = "";
                string thumbnail = "";
                lock (_wallpaperLock)
                {
                    _activeWallpaperIndex = index;
                    UpdateActiveStates(index);
                    _mockEngineRunning = true;

                    if (index > 0 && index <= _wallpapers.Count)
                    {
                        title = _wallpapers[index - 1].Title;
                        thumbnail = _wallpapers[index - 1].Thumbnail;
                        _activeWallpaperId = _wallpapers[index - 1].Id;
                    }
                }
                var newSession = new WallpaperSessionEventArgs(title, thumbnail, true, true);
                ActiveSession = newSession;
                SessionStateChanged?.Invoke(this, newSession);

                DiagnosticsService.SetAction("Wallpaper Service Idle / Launch complete (SafeDebugMode)");
                return await Task.FromResult(true);
            }

            // Single-flight gate: only one launch/swap at a time
            var generation = Interlocked.Increment(ref _launchGeneration);
            await _launchGate.WaitAsync();
            try
            {
                // Idea 4: GPU preference sync only matters when launching a *fresh* process.
                // A running engine cannot change its GPU mid-session, so skip the disk read
                // and registry query entirely on the IPC live-swap path.
                if (forceFreshLaunch || !IsEngineRunning())
                {
                    try
                    {
                        var syncSettings = _settingsStore.Load();
                        var registryPref = _gpuPreferenceService.GetGpuPreference(_appRunnerExePath);
                        if (registryPref != syncSettings.GpuPreference)
                        {
                            SyncGpuPreferences(syncSettings.GpuPreference);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WallpaperService] GPU preference registry sync failed: {ex.Message}");
                    }
                }

                // Try to swap in real-time over IPC Named Pipe first (skip if fresh launch is forced)
                if (!forceFreshLaunch && (await SendIpcCommandAsync($"swap {index}")) == "success")
                {
                    string targetPauseMode = pauseMode ?? ActivePauseProfile;
                    _ = UpdatePauseProfileAsync(targetPauseMode);

                    string? launchedId = null;
                    lock (_wallpaperLock)
                    {
                        _activeWallpaperIndex = index;
                        UpdateActiveStates(index);
                        if (index > 0 && index <= _wallpapers.Count)
                        {
                            launchedId = _wallpapers[index - 1].Id;
                            _activeWallpaperId = launchedId;
                        }
                    }

                    // Persist the confirmed active wallpaper ID, but only if this launch is
                    // still the current generation (a newer launch would have overwritten state)
                    if (launchedId != null && generation == _launchGeneration)
                    {
                        await PersistLastActiveWallpaperIdAsync(launchedId);
                    }

                    DiagnosticsService.SetAction("Wallpaper Service Idle / Swap via IPC complete");
                    return true;
                }


                if (!File.Exists(_appRunnerExePath))
                {
                    Debug.WriteLine($"AppRunner executable not found at: {_appRunnerExePath}");
                    DiagnosticsService.SetAction("Wallpaper Service Idle / Launch failed (Exe missing)");
                    return false;
                }

                string? launchedWallpaperId = null;
                lock (_wallpaperLock)
                {
                    _activeWallpaperIndex = index;
                    UpdateActiveStates(index);
                    if (index > 0 && index <= _wallpapers.Count)
                    {
                        launchedWallpaperId = _wallpapers[index - 1].Id;
                        _activeWallpaperId = launchedWallpaperId;
                    }
                }

                string mode = pauseMode ?? ActivePauseProfile;
                bool softDecode = softwareDecode ?? UseSoftwareDecoding;
                bool isMuted = _settingsStore.Load().MuteAudio;

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
                        string muteArg = $" --mute-audio {isMuted.ToString().ToLowerInvariant()}";
                        int currentPid = Environment.ProcessId;
                        string args = $"--detach --wallpaper {index} --silent --pause-mode {mode}{decodeArg}{muteArg} --ui-pid {currentPid}";

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

                // Only persist if this is still the current generation (no newer launch started)
                if (result && generation == _launchGeneration && launchedWallpaperId != null)
                {
                    await PersistLastActiveWallpaperIdAsync(launchedWallpaperId);
                }

                DiagnosticsService.SetAction("Wallpaper Service Idle / Launch complete");
                return result;
            }
            finally
            {
                _launchGate.Release();
            }
        }

        private async Task PersistLastActiveWallpaperIdAsync(string wallpaperId)
        {
            try
            {
                var settings = _settingsStore.Load();
                // Respect "remember last wallpaper": when off, don't persist a restore target.
                if (!settings.RememberLastWallpaper)
                {
                    return;
                }
                if (settings.LastActiveWallpaperId != wallpaperId)
                {
                    settings.LastActiveWallpaperId = wallpaperId;
                    _settingsStore.Save(settings);
                    Debug.WriteLine($"[WallpaperService] Persisted LastActiveWallpaperId: {wallpaperId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WallpaperService] Failed to persist LastActiveWallpaperId: {ex.Message}");
            }
            await Task.CompletedTask;
        }

    public async Task<bool> StopPlaybackAsync()
    {
        DiagnosticsService.SetAction("Wallpaper Service Stopping Playback");

        if (DebugFlags.SafeDebugMode)
        {
            Debug.WriteLine("[ISOLATE] StopPlaybackAsync requested.");
            lock (_wallpaperLock)
            {
                _activeWallpaperIndex = -1;
                UpdateActiveStates(-1);
                _mockEngineRunning = false;
            }

            var debugSession = new WallpaperSessionEventArgs("", "", false, false);
            ActiveSession = debugSession;
            SessionStateChanged?.Invoke(this, debugSession);

            DiagnosticsService.SetAction("Wallpaper Service Idle / Stop complete (SafeDebugMode)");
            return await Task.FromResult(true);
        }

        if (!File.Exists(_appRunnerExePath))
        {
            DiagnosticsService.SetAction("Wallpaper Service Idle / Stop failed (Exe missing)");
            return false;
        }

        lock (_wallpaperLock)
        {
            if (_activeWallpaperIndex >= 0)
            {
                _lastActiveWallpaperIndex = _activeWallpaperIndex;
            }
            _activeWallpaperIndex = -1;
            UpdateActiveStates(-1);
        }

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

        var newSession = new WallpaperSessionEventArgs("", "", false, false);
        if (ActiveSession == null || ActiveSession.IsActive)
        {
            ActiveSession = newSession;
            SessionStateChanged?.Invoke(this, newSession);
        }

        return result;
    }

    public int GetActiveWallpaperIndex() => _activeWallpaperIndex;

    private readonly SemaphoreSlim _gpuApplySemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Updates the GPU registry routing and restarts the Wallpaper Engine
    /// if it is currently running to ensure the preference takes effect immediately.
    /// </summary>
    public async Task ApplyGpuPreferenceAsync(GpuPreference mode)
    {
        await _gpuApplySemaphore.WaitAsync();
        try
        {
            // Signal observers (the watchdog) that an intentional restart is about to drop
            // the wallpaper window, so they don't fire "lost" and collide with the restart.
            EngineRestarting?.Invoke(this, EventArgs.Empty);

            try
            {
                // 1. Write GPU routing to Registry for both AppRunner and UI executables across all configurations
                SyncGpuPreferences(mode);

                // 2. Restart engine if currently running
                if (IsEngineRunning() && _activeWallpaperIndex > 0)
                {
                    int activeIndex = _activeWallpaperIndex;
                    await StopPlaybackAsync();

                    // Wait for the AppRunner process to fully release its GPU handle
                    await WaitForEngineExitAsync(2500);

                    // Brief additional cooldown before relaunching
                    await Task.Delay(300);

                    await LaunchWallpaperAsync(activeIndex);
                }
            }
            finally
            {
                // Re-arm observers regardless of outcome; they re-evaluate running state.
                EngineRestartCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _gpuApplySemaphore.Release();
        }
    }

    /// <summary>
    /// Synchronizes the DirectX UserGpuPreferences registry configuration for both the
    /// background Wallpaper Engine (AppRunner) and the primary UI process to ensure all
    /// child processes strictly adhere to the chosen GPU preference.
    /// </summary>
    public void SyncGpuPreferences(GpuPreference? explicitMode = null)
    {
        var mode = explicitMode ?? _settingsStore.Load().GpuPreference;
        var pathsToSync = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primary AppRunner executable path
        if (!string.IsNullOrEmpty(_appRunnerExePath) && File.Exists(_appRunnerExePath))
        {
            pathsToSync.Add(_appRunnerExePath);
        }

        // 2. Probed candidate paths for AppRunner and UI executables across build configurations
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string srcPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            var candidates = new[]
            {
                Path.Combine(baseDir, "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(baseDir, "WallpaperTurbo.UI.exe"),
                Path.Combine(baseDir, "WallpaperTurbo.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows", "win-x64", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Release", "net8.0-windows", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Release", "net8.0-windows", "win-x64", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Debug", "net8.0-windows", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Debug", "net8.0-windows", "win-x64", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Release", "net8.0-windows", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Release", "net8.0-windows", "win-x64", "WallpaperTurbo.AppRunner.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.UI", "bin", "Debug", "net8.0-windows", "WallpaperTurbo.UI.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.UI", "bin", "Release", "net8.0-windows", "WallpaperTurbo.UI.exe"),
                Path.Combine(srcPath, "WallpaperTurbo.UI", "bin", "x64", "Release", "net8.0-windows", "win-x64", "WallpaperTurbo.UI.exe")
            };

            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
            {
                pathsToSync.Add(currentExe);
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    pathsToSync.Add(candidate);
                }
            }

            if (pathsToSync.Count > 0)
            {
                _gpuPreferenceService.SetGpuPreferences(pathsToSync, mode);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WallpaperService] GPU preference synchronization encountered an issue: {ex.Message}");
        }
    }

    /// <summary>
    /// Polls until the WallpaperTurbo.AppRunner process has fully exited,
    /// or until <paramref name="timeoutMs"/> elapses. Call after
    /// <see cref="StopPlaybackAsync"/> before relaunching on a different GPU
    /// to ensure the GPU D3D handle is released before the new process starts.
    /// </summary>
    public async Task WaitForEngineExitAsync(int timeoutMs = 2500)
    {
        // Poll in 100 ms increments using Task.Delay (non-blocking) instead of Thread.Sleep,
        // so we don't occupy a thread-pool thread for the full timeout duration.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var procs = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
            bool anyRunning = procs.Length > 0;
            foreach (var p in procs) p.Dispose();

            if (!anyRunning) return;
            await Task.Delay(100);
        }
    }

    public async Task<bool> DeleteWallpaperAsync(WallpaperEntry wp)
    {
        // 1. If currently playing, stop playback first
        int index;
        lock (_wallpaperLock)
        {
            index = _wallpapers.IndexOf(wp);
        }

        if (wp.IsActive || _activeWallpaperIndex == index + 1)
        {
            await StopPlaybackAsync();
        }

        // 2. Call library service to delete manifest entry & disk folder
        bool success = await _libraryService.DeleteWallpaperAsync(wp.Id);
        if (success)
        {
            // Remove from the local cache list
            lock (_wallpaperLock)
            {
                _wallpapers.Remove(wp);
            }
        }
        return success;
    }

    public async Task<bool> UpdatePauseProfileAsync(string profile)
    {
        DiagnosticsService.SetAction($"Wallpaper Service updating pause profile to {profile} via IPC");
        // Map "Disabled" to "None" for AppRunner compatibility
        string mode = string.Equals(profile, "Disabled", StringComparison.OrdinalIgnoreCase) ? "None" : profile;
        return (await SendIpcCommandAsync($"pause-mode {mode}")) == "success";
    }

    public async Task<bool> PausePlaybackAsync()
    {
        DiagnosticsService.SetAction("Wallpaper Service Pausing Playback via IPC");
        return (await SendIpcCommandAsync("pause")) == "success";
    }

    public async Task<bool> ResumePlaybackAsync()
    {
        DiagnosticsService.SetAction("Wallpaper Service Resuming Playback via IPC");
        return (await SendIpcCommandAsync("play")) == "success";
    }

    public async Task<bool> SetMuteAsync(bool isMuted)
    {
        DiagnosticsService.SetAction($"Wallpaper Service updating mute state to {isMuted} via IPC");
        return (await SendIpcCommandAsync($"mute {isMuted.ToString().ToLowerInvariant()}")) == "success";
    }

    /// <summary>
    /// Tells a running engine which process now owns the UI, so its foreground watcher stops
    /// treating our own windows as something to pause for.
    /// </summary>
    /// <remarks>
    /// The engine otherwise learns this only from the <c>--ui-pid</c> argument it was launched
    /// with, which is passed exactly once. An engine that outlives a UI restart therefore keeps
    /// excluding a process id that no longer exists — so maximizing the app paused the very
    /// wallpaper it was displaying, and worse, a recycled process id could silently grant the
    /// exclusion to an unrelated program.
    /// </remarks>
    public async Task<bool> AnnounceUiProcessIdAsync()
    {
        DiagnosticsService.SetAction("Wallpaper Service announcing UI process id via IPC");
        return (await SendIpcCommandAsync($"ui-pid {Environment.ProcessId}")) == "success";
    }

    /// <summary>
    /// Determines the named pipe name used by the running AppRunner instance.
    /// Reads it from the active state file (written by AppRunner on startup/state updates)
    /// and caches it for subsequent IPC calls.
    /// </summary>
    private string GetIpcPipeName()
    {
        if (!string.IsNullOrEmpty(_cachedIpcPipeName))
            return _cachedIpcPipeName;

        try
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperTurbo");
            string stateFilePath = Path.Combine(appDataDir, "active_state.json");
            if (File.Exists(stateFilePath))
            {
                using var fs = new FileStream(stateFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = System.Text.Json.JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("IpcPipeName", out var nameProp))
                {
                    string? name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        _cachedIpcPipeName = name;
                        return _cachedIpcPipeName;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WallpaperService] Failed to read IPC pipe name: {ex.Message}");
        }

        // Fallback to the legacy hardcoded name for backward compatibility
        _cachedIpcPipeName = "WallpaperTurbo_IPC";
        return _cachedIpcPipeName;
    }

    /// <summary>
    /// Sends a raw command to the running engine over IPC. Overridable so tests never reach the
    /// real named pipe, which belongs to whatever AppRunner is alive on the developer's machine —
    /// an unpinned test would pause, swap or stop their actual wallpaper.
    /// </summary>
    internal Func<string, Task<string>>? IpcCommandOverride { get; set; }

    private async Task<string> SendIpcCommandAsync(string command)
    {
        if (IpcCommandOverride != null)
        {
            return await IpcCommandOverride(command);
        }

        try
        {
            string pipeName = GetIpcPipeName();
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await client.ConnectAsync(150); // 150ms timeout for instant responsiveness
            var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(command);

            var reader = new StreamReader(client);
            using var cts = new CancellationTokenSource(1500); // 1.5s timeout for confirmed response
            string? response = await reader.ReadLineAsync(cts.Token);
            return response ?? "error: timeout";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }
}

