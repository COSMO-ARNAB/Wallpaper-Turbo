using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Interop;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Media.Pipelines;
using WallpaperTurbo.Core.Models;
using WallpaperTurbo.Core.Rendering;
using WallpaperTurbo.Core.Services.Performance;
using WallpaperTurbo.Core.Services.Stability;

namespace WallpaperTurbo.Core.Wallpaper;

/// <summary>
/// Centralized engine orchestrator that synchronizes all rendering, watchers, 
/// hotswaps, recovery, and shutdown state transitions in Wallpaper Turbo.
/// Uses a strict sequential lock to eliminate resource races and memory leaks.
/// </summary>
public sealed class WallpaperEngineOrchestrator : IDisposable
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    
    // Core engine state
    private IntPtr _hwnd = IntPtr.Zero;
    private WallpaperSessionManager? _sessionManager;
    private ForegroundWindowWatcher? _foregroundWatcher;
    private ExplorerRestartMonitor? _restartMonitor;
    private CancellationTokenSource? _displayChangeCts;
    private readonly object _displayChangeLock = new();

    // Configuration parameters
    public WallpaperManifest Manifest { get; }
    public PauseMode PerformancePauseMode { get; }
    public VideoDecodeMode DecodeMode { get; }
    public string? VideoOutputModule { get; }
    public bool MemoryDiagnostics { get; }
    public string SuspendMode { get; }
    public int FileCachingMs { get; }
    public bool SkipDesktopAttach { get; }
    public bool IsSilent { get; }

    // Session and state trackers
    public int CurrentWallpaperIndex { get; private set; }
    public string? CustomVideoPath { get; private set; }
    private bool _isRecovering = false;
    private bool _disposed = false;

    public IntPtr WindowHandle => _hwnd;
    public WallpaperSessionManager? SessionManager => _sessionManager;

    public WallpaperEngineOrchestrator(
        WallpaperManifest manifest,
        PauseMode pauseMode,
        VideoDecodeMode decodeMode,
        string? videoOutputModule,
        bool memoryDiagnostics,
        string suspendMode,
        int fileCachingMs,
        bool skipDesktopAttach,
        bool isSilent)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        PerformancePauseMode = pauseMode;
        DecodeMode = decodeMode;
        VideoOutputModule = string.IsNullOrWhiteSpace(videoOutputModule) ? null : videoOutputModule.Trim();
        MemoryDiagnostics = memoryDiagnostics;
        SuspendMode = suspendMode;
        FileCachingMs = fileCachingMs;
        SkipDesktopAttach = skipDesktopAttach;
        IsSilent = isSilent;
    }

    /// <summary>
    /// Synchronously creates the render window, hooks up shell layers, spins up 
    /// the rendering pipeline, and starts watchers.
    /// </summary>
    public async Task<bool> InitializeAndStartAsync(int initialWallpaperIndex, string? customVideoPath, CancellationToken token)
    {
        await _stateLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CurrentWallpaperIndex = initialWallpaperIndex;
            CustomVideoPath = customVideoPath;
            _sessionManager = new WallpaperSessionManager();

            LogMemory("startup.before-window");

            MonitorInfo monitor = MonitorManager.GetPrimaryMonitor();

            // 1. Create native render window
            _hwnd = await NativeRenderWindow.CreateAsync(monitor).ConfigureAwait(false);
            if (_hwnd == IntPtr.Zero)
            {
                Console.Error.WriteLine("[Orchestrator] Error: Failed to create native render window.");
                return false;
            }

            LogMemory("render-window.created");

            // 2. Attach render window inside desktop WorkerW
            if (!SkipDesktopAttach)
            {
                var wm = new WindowsWallpaperManager(Console.WriteLine);
                bool attached = wm.AttachWindow(_hwnd, monitor);
                if (!attached)
                {
                    Console.Error.WriteLine("[Orchestrator] Error: Failed to attach render window to desktopWorkerW.");
                    return false;
                }
            }
            else
            {
                Console.WriteLine("[Orchestrator] Diagnostics Mode: Desktop attachment skipped; window remains top-level.");
            }

            LogMemory("desktop.attached");

            // 3. Align coordinates and set geometry topology
            ApplyWindowTopologyAndZOrder(_hwnd, monitor);

            // 4. Instantiate a clean media pipeline and play
            bool playStarted = await CreateAndPlaySessionAsync(monitor, token).ConfigureAwait(false);
            if (!playStarted)
            {
                return false;
            }

            // 5. Start stabilizers and observers
            StartWatchers();

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Orchestrator] Fatal engine initialization error: {ex}");
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Transition playback to a different wallpaper manifest index cleanly.
    /// Safely halts, disposes, GCs, and instantiates a brand new LibVLC pipeline.
    /// </summary>
    public async Task<bool> SwapWallpaperAsync(int newIndex)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (newIndex < 1 || newIndex > Manifest.Wallpapers.Count)
            {
                Console.Error.WriteLine($"[Orchestrator HotSwap] Error: Invalid wallpaper index {newIndex}.");
                return false;
            }

            WallpaperEntry newWallpaper = Manifest.Wallpapers[newIndex - 1];
            string videoPath = Path.Combine(AppContext.BaseDirectory, newWallpaper.Video);
            if (!File.Exists(videoPath))
            {
                Console.Error.WriteLine($"[Orchestrator HotSwap] Error: Video file not found: {videoPath}");
                return false;
            }

            Console.WriteLine($"\n[Orchestrator HotSwap] Initiating swap to wallpaper #{newIndex}: '{newWallpaper.Title}'...");

            // 1. Synchronously shut down and release the previous pipeline/VLC session
            if (_sessionManager != null)
            {
                Console.WriteLine("[Orchestrator HotSwap] Cleaning up old sessions synchronously...");
                _sessionManager.ShutdownAll();
            }

            // 2. Perform a thorough memory collection to release native textures
            TrimProcessMemory(forceEmptyWorkingSet: true);
            LogMemory("hotswap.after-cleanup");

            // 3. Update trackers
            CurrentWallpaperIndex = newIndex;
            CustomVideoPath = null; // Reset custom video

            // 4. Spin up fresh media pipeline on existing window HWND
            MonitorInfo monitor = MonitorManager.GetPrimaryMonitor();
            bool success = await CreateAndPlaySessionAsync(monitor, CancellationToken.None).ConfigureAwait(false);

            if (success)
            {
                Console.WriteLine($"[Orchestrator HotSwap] Successfully hot-swapped to '{newWallpaper.Title}'!");
            }
            return success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Orchestrator HotSwap] Exception: {ex.Message}");
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Transition playback to a custom external video file cleanly.
    /// Safely halts, disposes, GCs, and instantiates a brand new LibVLC pipeline.
    /// </summary>
    public async Task<bool> SetCustomVideoAsync(string videoPath)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(videoPath))
            {
                Console.Error.WriteLine($"[Orchestrator CustomSet] Error: Video file not found: {videoPath}");
                return false;
            }

            Console.WriteLine($"\n[Orchestrator CustomSet] Initiating swap to custom video: '{videoPath}'...");

            // 1. Coordinated clean shutdown of previous session
            if (_sessionManager != null)
            {
                Console.WriteLine("[Orchestrator CustomSet] Cleaning up old sessions synchronously...");
                _sessionManager.ShutdownAll();
            }

            // 2. Perform memory trim
            TrimProcessMemory(forceEmptyWorkingSet: true);
            LogMemory("customset.after-cleanup");

            // 3. Update path state
            CustomVideoPath = videoPath;

            // 4. Spin up new pipeline on existing window handle
            MonitorInfo monitor = MonitorManager.GetPrimaryMonitor();
            bool success = await CreateAndPlaySessionAsync(monitor, CancellationToken.None).ConfigureAwait(false);

            if (success)
            {
                Console.WriteLine($"[Orchestrator CustomSet] Successfully swapped to custom video: {videoPath}");
            }
            return success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Orchestrator CustomSet] Exception: {ex.Message}");
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Suspends rendering playback.
    /// </summary>
    public async Task PauseAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_sessionManager != null)
            {
                foreach (var session in _sessionManager.Sessions)
                {
                    session.Pause();
                }
            }
            Console.WriteLine("[Orchestrator] Rendering paused.");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Resumes rendering playback.
    /// </summary>
    public async Task ResumeAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_sessionManager != null)
            {
                foreach (var session in _sessionManager.Sessions)
                {
                    session.Play();
                }
            }
            Console.WriteLine("[Orchestrator] Rendering resumed.");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Adjusts layout mode for active wallpaper rendering.
    /// </summary>
    public async Task ApplyLayoutModeAsync(WallpaperLayoutMode mode)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_sessionManager != null)
            {
                foreach (var session in _sessionManager.Sessions)
                {
                    session.MediaPipeline.ApplyLayoutMode(mode);
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Sequenced creation and startup of a media pipeline session under lock.
    /// </summary>
    private async Task<bool> CreateAndPlaySessionAsync(MonitorInfo monitor, CancellationToken token)
    {
        // Internal helper: Caller MUST hold _stateLock
        try
        {
            string videoPath;
            WallpaperEntry wallpaper;

            if (!string.IsNullOrEmpty(CustomVideoPath))
            {
                wallpaper = new WallpaperEntry
                {
                    Id = "custom",
                    Title = Path.GetFileNameWithoutExtension(CustomVideoPath),
                    Author = "Custom Video",
                    Video = CustomVideoPath,
                    LayoutMode = "Fill"
                };
                videoPath = CustomVideoPath;
            }
            else
            {
                wallpaper = Manifest.Wallpapers[CurrentWallpaperIndex - 1];
                videoPath = Path.Combine(AppContext.BaseDirectory, wallpaper.Video);
            }

            if (!File.Exists(videoPath))
            {
                Console.Error.WriteLine($"[Orchestrator] Target video source not found: {videoPath}");
                return false;
            }

            Console.WriteLine($"[Orchestrator] Spinning up clean HardwareDecodePipeline for: {videoPath}");

            // 1. Create the hardware pipeline fresh to clear any cached textures or D3D11 swapchains
            var pipeline = new HardwareDecodePipeline(
                DecodeMode,
                VideoOutputModule,
                suspendAsPause: SuspendMode == "pause",
                fileCachingMs: FileCachingMs);

            LogMemory("pipeline.before-initialize");
            pipeline.Initialize(_hwnd);
            LogMemory("pipeline.after-initialize");

            pipeline.LoadMedia(videoPath);
            LogMemory("media.after-load");

            var session = new WallpaperSession(_hwnd, wallpaper, pipeline, monitor);
            _sessionManager?.AddSession(session);

            LogMemory("playback.before-play");
            session.Play();
            LogMemory("playback.after-play");

            // 2. Allow a brief delay for VLC video output window threads to initialize, then apply click-through styling
            await Task.Delay(500, token).ConfigureAwait(false);
            WindowUtil.MakeChildrenTransparent(_hwnd);

            // 3. Final alignment check
            ApplyWindowTopologyAndZOrder(_hwnd, monitor);
            LogMemory("desktop.zorder-enforced");

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Orchestrator] Pipeline spin-up failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts background stabilizers and watcher classes.
    /// </summary>
    private void StartWatchers()
    {
        // 1. Explorer Shell Restart Watcher
        _restartMonitor = new ExplorerRestartMonitor();
        _restartMonitor.RestartDetected += () =>
        {
            // Recover sequentially in background to avoid blocking WndProc STA message thread
            _ = Task.Run(() => HandleExplorerRestartAsync());
        };

        // 2. Display Topology Adjuster
        _restartMonitor.DisplaySettingsChanged += HandleDisplaySettingsChanged;

        // 3. Foreground Visibility Watcher
        _foregroundWatcher = new ForegroundWindowWatcher(PerformancePauseMode);
        _foregroundWatcher.VisibilityChanged += OnVisibilityChanged;

        Console.WriteLine($"[Orchestrator Watchers] Observers active. Performance Pause Mode: {PerformancePauseMode}");
        LogMemory("watchers.started");
    }

    /// <summary>
    /// Sequenced recovery loop execution when Windows Explorer restart crashes are intercepted.
    /// </summary>
    private async Task HandleExplorerRestartAsync()
    {
        if (_isRecovering) return;
        _isRecovering = true;

        Console.WriteLine("\n[Orchestrator Recovery] Intercepted Explorer restart! Enforcing sequential recovery...");

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // 1. Tear down previous sessions
            if (_sessionManager != null)
            {
                Console.WriteLine("[Orchestrator Recovery] Stopping active rendering...");
                _sessionManager.ShutdownAll();
            }

            // 2. Forcefully close and nullify old window HWND
            if (_hwnd != IntPtr.Zero)
            {
                Console.WriteLine("[Orchestrator Recovery] Destroying old render window...");
                NativeRenderWindow.Shutdown(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            // 3. Clean up native memory blocks immediately
            TrimProcessMemory(forceEmptyWorkingSet: true);
            LogMemory("recovery.after-destruction");

            // 4. Repeatedly poll desktop WorkerW to settle and rebuild shell attachment
            bool success = false;
            int maxAttempts = 10;
            MonitorInfo freshMonitor = MonitorManager.GetPrimaryMonitor();

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Console.WriteLine($"[Orchestrator Recovery] Re-attachment attempt {attempt} of {maxAttempts}...");

                    // Safe settle delay
                    await Task.Delay(1000).ConfigureAwait(false);

                    IntPtr progman = DesktopUtil.GetProgman();
                    IntPtr workerW = DesktopUtil.GetDesktopWorkerW();

                    if (progman == IntPtr.Zero || workerW == IntPtr.Zero)
                    {
                        Console.WriteLine("[Orchestrator Recovery] Shell components (Progman/WorkerW) not fully initialized yet. Waiting...");
                        continue;
                    }

                    freshMonitor = MonitorManager.GetPrimaryMonitor();

                    // Recreate native window
                    _hwnd = await NativeRenderWindow.CreateAsync(freshMonitor).ConfigureAwait(false);
                    if (_hwnd == IntPtr.Zero)
                    {
                        Console.Error.WriteLine("[Orchestrator Recovery] Render window recreation failed. Retrying...");
                        continue;
                    }

                    // Attach into desktop WorkerW
                    var freshManager = new WindowsWallpaperManager();
                    bool freshAttached = freshManager.AttachWindow(_hwnd, freshMonitor);
                    if (!freshAttached)
                    {
                        Console.Error.WriteLine("[Orchestrator Recovery] Desktop attachment failed. Retrying...");
                        NativeRenderWindow.Shutdown(_hwnd);
                        _hwnd = IntPtr.Zero;
                        continue;
                    }

                    success = true;
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Orchestrator Recovery] Exception on attempt {attempt}: {ex.Message}");
                    if (_hwnd != IntPtr.Zero)
                    {
                        NativeRenderWindow.Shutdown(_hwnd);
                        _hwnd = IntPtr.Zero;
                    }
                }
            }

            if (!success)
            {
                Console.Error.WriteLine("[Orchestrator Recovery] Desktop shell recovery failed. Max attempts reached.");
                return;
            }

            // 5. Position and align window to new WorkerW parent
            ApplyWindowTopologyAndZOrder(_hwnd, freshMonitor);

            // 6. Instantiate a fresh pipeline and play media
            bool playSuccess = await CreateAndPlaySessionAsync(freshMonitor, CancellationToken.None).ConfigureAwait(false);
            if (playSuccess)
            {
                Console.WriteLine("[Orchestrator Recovery] Sequenced desktop shell recovery successfully completed!");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Orchestrator Recovery] Fatal recovery exception: {ex.Message}");
        }
        finally
        {
            _isRecovering = false;
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Debounced layout adjuster called when multi-monitor or display resolution adjustments are made.
    /// </summary>
    private void HandleDisplaySettingsChanged()
    {
        CancellationToken token;
        lock (_displayChangeLock)
        {
            _displayChangeCts?.Cancel();
            _displayChangeCts?.Dispose();
            _displayChangeCts = new CancellationTokenSource();
            token = _displayChangeCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("[Orchestrator DisplayChange] Display change detected. Debouncing layout adjustment...");
                await Task.Delay(500, token).ConfigureAwait(false);

                await _stateLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (_hwnd == IntPtr.Zero || _sessionManager == null) return;

                    Console.WriteLine("[Orchestrator DisplayChange] Rearranging topology coordinates under lock...");

                    var freshMonitors = MonitorManager.GetMonitors();
                    foreach (var session in _sessionManager.Sessions)
                    {
                        MonitorInfo? freshMonitor = null;
                        foreach (var m in freshMonitors)
                        {
                            if (m.DeviceName == session.Monitor.DeviceName)
                            {
                                freshMonitor = m;
                                break;
                            }
                        }

                        freshMonitor ??= MonitorManager.GetPrimaryMonitor();

                        Console.WriteLine($"[Orchestrator DisplayChange] Moving rendering window to monitor '{freshMonitor.DeviceName}' bounds ({freshMonitor.Width}x{freshMonitor.Height})...");

                        // Resize and align z-order
                        ApplyWindowTopologyAndZOrder(session.WindowHandle, freshMonitor);

                        // Update references and recalculate layout aspect/crop ratios inside pipeline
                        session.UpdateMonitor(freshMonitor);
                        session.MediaPipeline.ApplyLayoutMode(session.Wallpaper.GetLayoutMode());
                    }

                    Console.WriteLine("[Orchestrator DisplayChange] Debounced display adjustment finished.");
                }
                finally
                {
                    _stateLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored, coalesced by a subsequent display change
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Orchestrator DisplayChange] Coordinate adjustment error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Observes focused and maximized screen covering windows to trigger resource suspensions.
    /// </summary>
    private void OnVisibilityChanged(bool isObscured)
    {
        if (_sessionManager == null) return;

        try
        {
            if (isObscured)
            {
                string reason = PerformancePauseMode == PauseMode.Focused ? "focused application" : "fullscreen/maximized application";
                Console.WriteLine($"[Orchestrator Performance] Screen obscured by {reason}. Suspending rendering...");

                foreach (var session in _sessionManager.Sessions)
                {
                    if (PerformancePauseMode == PauseMode.Maximized)
                    {
                        session.Suspend();
                    }
                    else
                    {
                        session.Pause();
                    }
                }

                TrimProcessMemory(forceEmptyWorkingSet: false);
                LogMemory("performance.paused");
            }
            else
            {
                Console.WriteLine("[Orchestrator Performance] Screen is visible. Resuming rendering...");

                foreach (var session in _sessionManager.Sessions)
                {
                    if (PerformancePauseMode == PauseMode.Maximized)
                    {
                        session.Resume();
                    }
                    else
                    {
                        session.Play();
                    }
                }

                LogMemory("performance.resumed");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Orchestrator Performance] Error handling focus shift: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper that sets relative parent coordinates, sets size dimensions, and persists z-order layers.
    /// </summary>
    private void ApplyWindowTopologyAndZOrder(IntPtr hwnd, MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero) return;

        // 1. Calculate relative parenting offset
        int relX = monitor.X;
        int relY = monitor.Y;
        IntPtr parent = NativeMethods.GetParent(hwnd);
        if (parent != IntPtr.Zero)
        {
            NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
            NativeMethods.MapWindowPoints(IntPtr.Zero, parent, ref prct, 2);
            relX = prct.Left;
            relY = prct.Top;
        }

        // 2. Set coordinate positions
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            relX,
            relY,
            monitor.Width,
            monitor.Height,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                NativeMethods.SetWindowPosFlags.SWP_NOZORDER));

        // 3. Apply persistent window layering (z-order)
        if (DesktopUtil.IsRaisedDesktop())
        {
            IntPtr shellView = DesktopUtil.GetDesktopShellView();
            IntPtr progman = DesktopUtil.GetProgman();
            IntPtr workerW = DesktopUtil.GetDesktopWorkerW();

            NativeRenderWindow.ShellViewHandle = shellView;

            if (shellView != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    shellView,
                    relX,
                    relY,
                    monitor.Width,
                    monitor.Height,
                    (uint)(
                        NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                        NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));
            }
            else
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    relX,
                    relY,
                    monitor.Width,
                    monitor.Height,
                    (uint)(
                        NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                        NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                        NativeMethods.SetWindowPosFlags.SWP_NOZORDER));
            }

            if (progman != IntPtr.Zero && workerW != IntPtr.Zero)
            {
                IntPtr lastChild = WindowUtil.GetLastChildWindow(progman);
                if (lastChild != workerW)
                {
                    NativeMethods.SetWindowPos(
                        workerW,
                        NativeMethods.HWND_BOTTOM,
                        0,
                        0,
                        0,
                        0,
                        (uint)(
                            NativeMethods.SetWindowPosFlags.SWP_NOMOVE |
                            NativeMethods.SetWindowPosFlags.SWP_NOSIZE |
                            NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                            NativeMethods.SetWindowPosFlags.SWP_NOOWNERZORDER |
                            NativeMethods.SetWindowPosFlags.SWP_NOSENDCHANGING));
                }
            }
        }
        else
        {
            WindowUtil.SendToBottom(hwnd);

            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_BOTTOM,
                relX,
                relY,
                monitor.Width,
                monitor.Height,
                (uint)(
                    NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                    NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                    NativeMethods.SetWindowPosFlags.SWP_NOOWNERZORDER |
                    NativeMethods.SetWindowPosFlags.SWP_NOSENDCHANGING));
        }
    }

    /// <summary>
    /// Synchronously disposes active watchers, wallpaper sessions, pipelines, and window handles.
    /// </summary>
    public async Task ShutdownAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Console.WriteLine("[Orchestrator] Enforcing coordinated structured engine shutdown...");

            // 1. Kill watchers
            try { _foregroundWatcher?.Dispose(); } catch { }
            _foregroundWatcher = null;

            try { _restartMonitor?.Dispose(); } catch { }
            _restartMonitor = null;

            lock (_displayChangeLock)
            {
                try { _displayChangeCts?.Cancel(); _displayChangeCts?.Dispose(); } catch { }
                _displayChangeCts = null;
            }

            // 2. Dispose rendering sessions and pipeline
            if (_sessionManager != null)
            {
                Console.WriteLine("[Orchestrator] Stopping active sessions...");
                _sessionManager.ShutdownAll();
                _sessionManager.Dispose();
                _sessionManager = null;
            }

            // 3. Destroy window handle
            if (_hwnd != IntPtr.Zero)
            {
                Console.WriteLine("[Orchestrator] Shutting down native window handle...");
                NativeRenderWindow.Shutdown(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            // 4. Forceful memory trim
            TrimProcessMemory(forceEmptyWorkingSet: true);
            LogMemory("shutdown.final");

            Console.WriteLine("[Orchestrator] Engine shutdown successfully completed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Orchestrator] Error during engine shutdown: {ex.Message}");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Immediate GC memory trim wrapper.
    /// </summary>
    private void TrimProcessMemory(bool forceEmptyWorkingSet)
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (forceEmptyWorkingSet)
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                NativeMethods.EmptyWorkingSet(process.Handle);
            }
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[Orchestrator Memory] Trim failed: {ex.Message}"); } catch { }
        }
    }

    /// <summary>
    /// Writes detailed GC diagnostics to standard output.
    /// </summary>
    private void LogMemory(string checkpoint, bool force = false)
    {
        if (!MemoryDiagnostics && !force) return;

        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();

            GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();

            Console.WriteLine(
                $"[Orchestrator Memory:{checkpoint}] " +
                $"Private={FormatBytes(process.PrivateMemorySize64)}, " +
                $"WorkingSet={FormatBytes(process.WorkingSet64)}, " +
                $"Virtual={FormatBytes(process.VirtualMemorySize64)}, " +
                $"Managed={FormatBytes(GC.GetTotalMemory(false))}, " +
                $"GCHeap={FormatBytes(gcInfo.HeapSizeBytes)}, " +
                $"Committed={FormatBytes(gcInfo.TotalCommittedBytes)}, " +
                $"Handles={process.HandleCount}, " +
                $"Threads={process.Threads.Count}");
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[Orchestrator Memory:{checkpoint}] Diagnostics error: {ex.Message}"); } catch { }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clean up synchronously, waiting up to 2 seconds for active loops to finish
        var shutdownTask = Task.Run(() => ShutdownAsync());
        shutdownTask.Wait(2000);

        _stateLock.Dispose();
    }
}
