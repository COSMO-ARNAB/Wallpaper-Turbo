using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Services;

public class StartupResult
{
    public bool IsEngineRunning { get; init; }
    public WallpaperEntry? ActiveWallpaper { get; init; }
    public bool TimedOut { get; init; }
    public string? ErrorMessage { get; init; }
}

public class WallpaperStartupCoordinator
{
    /// <summary>Gap between readiness polls while waiting for the engine to come up.</summary>
    internal const int ReadinessPollIntervalMs = 100;

    /// <summary>Number of readiness polls before giving up.</summary>
    internal const int ReadinessMaxAttempts = 100;

    /// <summary>
    /// Total readiness budget, derived from the poll interval and attempt count so the value used
    /// in logs can never drift from the value actually waited.
    /// </summary>
    internal static readonly TimeSpan ReadinessTimeout =
        TimeSpan.FromMilliseconds((long)ReadinessPollIntervalMs * ReadinessMaxAttempts);

    private readonly IWallpaperLibraryService _libraryService;
    private readonly WallpaperService _wallpaperService;
    private readonly ISettingsStore _settingsStore;
    private readonly object _startupLock = new();
    private Task<StartupResult>? _startupTask;

    public WallpaperStartupCoordinator(
        IWallpaperLibraryService libraryService,
        WallpaperService wallpaperService,
        ISettingsStore settingsStore)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public Task<StartupResult> EnsureWallpaperRunningAsync(CancellationToken ct = default)
    {
        StartupDiagnostics.LogWithMemory("EnsureWallpaperRunningAsync START");
        lock (_startupLock)
        {
            if (_startupTask != null)
            {
                StartupDiagnostics.LogWithMemory("EnsureWallpaperRunningAsync returning cached task");
                return _startupTask;
            }

            _startupTask = RunStartupAndCacheResultAsync(ct);
            return _startupTask;
        }
    }

    private async Task<StartupResult> RunStartupAndCacheResultAsync(CancellationToken ct)
    {
        var swEnsure = Stopwatch.StartNew();
        StartupDiagnostics.LogWithMemory("RunStartupAndCacheResultAsync START");
        var result = await RunStartupAsync(ct);
        StartupDiagnostics.LogWithMemory($"RunStartupAndCacheResultAsync END in {swEnsure.ElapsedMilliseconds}ms: running={result.IsEngineRunning}, timeout={result.TimedOut}");

        // Only cache a healthy result. On timeout/failure, clear the cached task so a
        // later call (e.g. the UI's Retry button) actually re-runs the startup sequence.
        if (!result.IsEngineRunning)
        {
            lock (_startupLock)
            {
                _startupTask = null;
            }
        }

        StartupDiagnostics.LogWithMemory($"EnsureWallpaperRunningAsync END in {swEnsure.ElapsedMilliseconds}ms");
        return result;
    }

    private async Task<StartupResult> RunStartupAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Log("Startup coordinator starting");

        try
        {
            // 1. Load library (blocking for startup)
            Log("Loading wallpaper library");
            var wallpapers = await _libraryService.GetWallpapersAsync();
            Log($"Library loaded: {wallpapers.Count} wallpapers in {sw.ElapsedMilliseconds}ms");

            if (wallpapers.Count == 0)
            {
                Log("No wallpapers available");
                return new StartupResult
                {
                    IsEngineRunning = false,
                    ActiveWallpaper = null,
                    TimedOut = false,
                    ErrorMessage = "No wallpapers available"
                };
            }

            // 2. Read settings: restore target + behavior gates
            var settings = _settingsStore.Load();
            var lastActiveId = settings.LastActiveWallpaperId;
            var autoStart = settings.AutoStartWallpaperEngine;
            var rememberLast = settings.RememberLastWallpaper;
            Log($"Settings: LastActiveWallpaperId={lastActiveId ?? "(none)"}, AutoStart={autoStart}, RememberLast={rememberLast}");

            // 3. If AppRunner is already running and valid, always adopt it — reflecting a
            // genuinely-running engine is not "starting" it, so this ignores the auto-start gate.
            // H1 cold-start gate fix: instrument TryAdopt so 30s budget is measurable; already-running
            // path skips LaunchWithReadinessCheck entirely (no double 10s).
            var tryAdoptSw = Stopwatch.StartNew();
            StartupDiagnostics.LogWithMemory("TryAdoptExistingSession START");
            var existingSession = await TryAdoptExistingSessionAsync(wallpapers, ct);
            StartupDiagnostics.LogWithMemory($"TryAdoptExistingSession END in {tryAdoptSw.ElapsedMilliseconds}ms: {(existingSession != null ? "adopted " + existingSession.Title : "no session")}");
            if (existingSession != null)
            {
                Log($"Adopted existing session: {existingSession.Title} in {sw.ElapsedMilliseconds}ms");
                StartupDiagnostics.LogWithMemory($"Startup coordinator adopted existing session in {sw.ElapsedMilliseconds}ms");
                return new StartupResult
                {
                    IsEngineRunning = true,
                    ActiveWallpaper = existingSession,
                    TimedOut = false
                };
            }

            // 4. No running engine. If auto-start is disabled, stop here — do NOT launch.
            if (!autoStart)
            {
                Log("Auto-start disabled — leaving engine stopped");
                return new StartupResult
                {
                    IsEngineRunning = false,
                    ActiveWallpaper = null,
                    TimedOut = false
                };
            }

            // 5. Pick the wallpaper to launch. The recent-history fallback is the primary
            // restore signal — most-recently-used first — so restore "just works" even on a
            // first run after upgrade before LastActiveWallpaperId is trusted. The trusted
            // LastActiveWallpaperId wins when present (engine-confirmed playback), and
            // "remember last wallpaper" off disables both restore paths (first library entry).
            var wallpaperToLaunch = ResolveWallpaperToLaunch(
                wallpapers, lastActiveId, rememberLast, GetRecentHistoryPath());
            Log($"Launching wallpaper: {wallpaperToLaunch.Title} (ID: {wallpaperToLaunch.Id})");

            var launchSw = Stopwatch.StartNew();
            StartupDiagnostics.LogWithMemory($"LaunchWithReadinessCheck START: {wallpaperToLaunch.Title} (ID: {wallpaperToLaunch.Id})");
            var launchResult = await LaunchWithReadinessCheckAsync(wallpaperToLaunch, wallpapers, ct);
            StartupDiagnostics.LogWithMemory($"LaunchWithReadinessCheck END in {launchSw.ElapsedMilliseconds}ms: running={launchResult.IsEngineRunning}, timeout={launchResult.TimedOut}");
            Log($"Launch completed in {sw.ElapsedMilliseconds}ms: running={launchResult.IsEngineRunning}, timeout={launchResult.TimedOut}");

            return launchResult;
        }
        catch (OperationCanceledException)
        {
            Log("Startup cancelled");
            return new StartupResult
            {
                IsEngineRunning = false,
                ActiveWallpaper = null,
                TimedOut = false,
                ErrorMessage = "Startup cancelled"
            };
        }
        catch (Exception ex)
        {
            Log($"Startup failed: {ex.Message}");
            return new StartupResult
            {
                IsEngineRunning = false,
                ActiveWallpaper = null,
                TimedOut = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<WallpaperEntry?> TryAdoptExistingSessionAsync(
        IReadOnlyList<WallpaperEntry> wallpapers,
        CancellationToken ct)
    {
        // Check if AppRunner process exists
        var runnerProcesses = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
        var runnerPids = runnerProcesses.Select(p => p.Id).ToHashSet();
        foreach (var p in runnerProcesses) p.Dispose();

        if (runnerPids.Count == 0)
        {
            Log("No AppRunner process found");
            return null;
        }

        Log($"Found {runnerPids.Count} AppRunner process(es)");

        // Validate active_state.json against any running AppRunner PID
        var state = await ReadAndValidateStateFileAsync(runnerPids, ct);
        if (state == null)
        {
            Log("active_state.json validation failed — stale state");
            _wallpaperService.ClearCachedIpcPipeName();
            return null;
        }

        // IPC ping to confirm responsiveness
        var ipcOk = await _wallpaperService.PingIpcAsync();
        if (!ipcOk)
        {
            Log("IPC ping failed — engine not responsive");
            _wallpaperService.ClearCachedIpcPipeName();
            return null;
        }

        // Find the wallpaper by index from state
        if (state.ActiveWallpaperIndex > 0 && state.ActiveWallpaperIndex <= wallpapers.Count)
        {
            var wp = wallpapers[state.ActiveWallpaperIndex - 1];
            // Persist the confirmed active wallpaper ID
            await PersistLastActiveWallpaperIdAsync(wp.Id);
            return wp;
        }

        Log($"State index {state.ActiveWallpaperIndex} out of range (0-{wallpapers.Count})");
        return null;
    }

    private async Task<ActiveStateFile?> ReadAndValidateStateFileAsync(IReadOnlyCollection<int> validPids, CancellationToken ct)
    {
        try
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallpaperTurbo");
            var stateFilePath = Path.Combine(appDataDir, "active_state.json");

            if (!File.Exists(stateFilePath))
            {
                Log("active_state.json does not exist");
                return null;
            }

            using var fs = new FileStream(stateFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
            var root = doc.RootElement;

            // Validate ProcessId matches a running AppRunner
            int statePid;
            if (root.TryGetProperty("ProcessId", out var pidProp))
            {
                statePid = pidProp.GetInt32();
                if (!validPids.Contains(statePid))
                {
                    Log($"State PID {statePid} not among running AppRunner PIDs — stale");
                    return null;
                }
            }
            else
            {
                Log("State file missing ProcessId — stale format");
                return null;
            }

            // Validate UpdatedAtUtc is recent (within 5 minutes)
            if (root.TryGetProperty("UpdatedAtUtc", out var updatedProp))
            {
                if (DateTime.TryParse(updatedProp.GetString(), out var updated))
                {
                    if (DateTime.UtcNow - updated > TimeSpan.FromMinutes(5))
                    {
                        Log($"State file older than 5 minutes ({updated}) — stale");
                        return null;
                    }
                }
            }

            // Parse the rest
            var state = new ActiveStateFile
            {
                ProcessId = statePid,
                ActiveWallpaperIndex = root.TryGetProperty("ActiveWallpaperIndex", out var idxProp)
                    ? idxProp.GetInt32()
                    : -1,
                ActiveWallpaperTitle = root.TryGetProperty("ActiveWallpaperTitle", out var titleProp)
                    ? titleProp.GetString() ?? string.Empty
                    : string.Empty,
                IsPlaying = root.TryGetProperty("IsPlaying", out var playProp) && playProp.GetBoolean(),
                IpcPipeName = root.TryGetProperty("IpcPipeName", out var pipeProp)
                    ? pipeProp.GetString() ?? string.Empty
                    : string.Empty
            };

            Log($"Validated state: index={state.ActiveWallpaperIndex}, title={state.ActiveWallpaperTitle}, playing={state.IsPlaying}, pipe={state.IpcPipeName}");
            return state;
        }
        catch (Exception ex)
        {
            Log($"Failed to read/validate state file: {ex.Message}");
            return null;
        }
    }

    private async Task<StartupResult> LaunchWithReadinessCheckAsync(
        WallpaperEntry wallpaperToLaunch,
        IReadOnlyList<WallpaperEntry> wallpapers,
        CancellationToken ct)
    {
        // Find index (1-based for AppRunner)
        var index = wallpapers.ToList().IndexOf(wallpaperToLaunch) + 1;
        if (index <= 0)
        {
            index = 1; // fallback to first
        }

        // Launch the wallpaper
        var launched = await _wallpaperService.LaunchWallpaperAsync(index);
        if (!launched)
        {
            return new StartupResult
            {
                IsEngineRunning = false,
                ActiveWallpaper = null,
                TimedOut = false,
                ErrorMessage = "Failed to launch AppRunner"
            };
        }

        // Poll for readiness (kept at 100*100ms for reliability; cold-start budget saved elsewhere via offloading).
        for (int attempt = 0; attempt < ReadinessMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Check if process exists
            var processes = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
            var pids = processes.Select(p => p.Id).ToHashSet();
            foreach (var p in processes) p.Dispose();

            if (pids.Count > 0)
            {
                // Try IPC ping
                var ipcOk = await _wallpaperService.PingIpcAsync();
                if (ipcOk)
                {
                    // Read state to confirm
                    var state = await ReadAndValidateStateFileAsync(pids, ct);
                    if (state != null && state.ActiveWallpaperIndex == index)
                    {
                        await PersistLastActiveWallpaperIdAsync(wallpaperToLaunch.Id);
                        return new StartupResult
                        {
                            IsEngineRunning = true,
                            ActiveWallpaper = wallpaperToLaunch,
                            TimedOut = false
                        };
                    }
                }
            }

            await Task.Delay(ReadinessPollIntervalMs, ct);
        }

        // Timeout — engine is still starting
        Log($"Readiness timeout after {ReadinessTimeout.TotalSeconds:0}s");
        StartupDiagnostics.LogWithMemory($"LaunchWithReadinessCheck TIMEOUT after {ReadinessTimeout.TotalSeconds:0}s");
        return new StartupResult
        {
            IsEngineRunning = false,
            ActiveWallpaper = wallpaperToLaunch,
            TimedOut = true,
            ErrorMessage = "Wallpaper engine is still starting. Please wait or click Retry."
        };
    }

    internal static WallpaperEntry? FindWallpaperById(IReadOnlyList<WallpaperEntry> wallpapers, string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        return wallpapers.FirstOrDefault(w => w.Id == id);
    }

    /// <summary>
    /// Best-effort restore from recent_history.json (most-recently-used first). Used only when
    /// there is no trusted LastActiveWallpaperId. Returns the first history entry that still
    /// resolves to a wallpaper in the current library; null if none do.
    /// </summary>
    private WallpaperEntry? FindMostRecentlyUsed(IReadOnlyList<WallpaperEntry> wallpapers)
        => FindMostRecentlyUsed(wallpapers, GetRecentHistoryPath());

    /// <summary>
    /// Testable variant: caller supplies the history file path so tests can point at a temp file.
    /// </summary>
    internal static WallpaperEntry? FindMostRecentlyUsed(IReadOnlyList<WallpaperEntry> wallpapers, string historyPath)
    {
        try
        {
            if (!File.Exists(historyPath))
            {
                LogStatic("recent_history.json does not exist");
                return null;
            }

            var json = File.ReadAllText(historyPath);
            var ids = JsonSerializer.Deserialize<List<string>>(json);
            if (ids == null || ids.Count == 0)
            {
                LogStatic("recent_history.json is empty");
                return null;
            }

            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                var wp = wallpapers.FirstOrDefault(w => w.Id == id);
                if (wp != null)
                {
                    LogStatic($"Most-recently-used wallpaper from history: {wp.Title} (ID: {wp.Id})");
                    return wp;
                }
            }

            LogStatic("No recent_history entry resolved to a library wallpaper");
            return null;
        }
        catch (Exception ex)
        {
            LogStatic($"Failed to read recent_history.json: {ex.Message}");
            return null;
        }
    }

    private string GetRecentHistoryPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperTurbo",
            "recent_history.json");

    /// <summary>
    /// Resolves which wallpaper to auto-launch. Resolution order (when rememberLast is on):
    ///   1. Trusted LastActiveWallpaperId (written only when the engine confirmed playback)
    ///   2. Most-recently-used entry from recent_history.json (primary restore signal —
    ///      works even before LastActiveWallpaperId is trusted, e.g. first run after upgrade)
    /// When rememberLast is off, or no usage signal resolves, falls back to the first library
    /// entry — never an arbitrary hardcoded name.
    /// Extracted and made internal so the fallback logic is unit-testable in isolation.
    /// </summary>
    internal static WallpaperEntry ResolveWallpaperToLaunch(
        IReadOnlyList<WallpaperEntry> wallpapers,
        string? lastActiveId,
        bool rememberLast,
        string historyPath)
    {
        if (wallpapers.Count == 0)
        {
            throw new ArgumentException("No wallpapers available to resolve", nameof(wallpapers));
        }

        if (rememberLast)
        {
            // 1. Engine-confirmed trusted id wins when present and resolvable.
            var byId = FindWallpaperById(wallpapers, lastActiveId);
            if (byId != null)
            {
                return byId;
            }

            // 2. Most-recently-used is the primary restore signal otherwise.
            var byHistory = FindMostRecentlyUsed(wallpapers, historyPath);
            if (byHistory != null)
            {
                return byHistory;
            }
        }

        // Last resort: first library entry by position, never a hardcoded name.
        return wallpapers[0];
    }

    private static void LogStatic(string message)
    {
        Debug.WriteLine($"[WallpaperStartupCoordinator] {message}");
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
                Log($"Persisted LastActiveWallpaperId: {wallpaperId}");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to persist LastActiveWallpaperId: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    private void Log(string message)
    {
        Debug.WriteLine($"[WallpaperStartupCoordinator] {message}");
    }

    private class ActiveStateFile
    {
        public int ProcessId { get; set; }
        public int ActiveWallpaperIndex { get; set; }
        public string ActiveWallpaperTitle { get; set; } = string.Empty;
        public bool IsPlaying { get; set; }
        public string IpcPipeName { get; set; } = string.Empty;
    }
}