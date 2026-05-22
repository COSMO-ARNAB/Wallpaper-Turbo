// Program.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Hardware.Models;
using WallpaperTurbo.Core.Interop;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Media.Pipelines;
using WallpaperTurbo.Core.Models;
using WallpaperTurbo.Core.Rendering;
using WallpaperTurbo.Core.Rendering.Host;
using WallpaperTurbo.Core.Wallpaper;
using WallpaperTurbo.Core.Services.Stability;
using WallpaperTurbo.Core.Services.Performance;

namespace WallpaperTurbo.AppRunner;

internal static class Program
{
    private static System.IO.StreamWriter? _logWriter;
    private static int _finalWallpaperIndex = 1;
    private static PauseMode _pauseMode = PauseMode.Maximized;
    private static bool _useSoftwareDecode;
    private static string? _videoOutputModule;
    private static bool _memoryDiagnostics;
    private static WallpaperSessionManager? _sessionManager;
    private static IMediaPipeline? _activePipeline;
    private static IntPtr _hwnd = IntPtr.Zero;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    private delegate bool ConsoleCtrlDelegate(int ctrlType);

    private static ConsoleCtrlDelegate? _consoleCtrlHandler;
    private static int _isDetaching = 0;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private static bool OnConsoleCtrl(int ctrlType)
    {
        // 2: CTRL_CLOSE_EVENT, 5: CTRL_LOGOFF_EVENT, 6: CTRL_SHUTDOWN_EVENT
        if (ctrlType == 2 || ctrlType == 5 || ctrlType == 6)
        {
            try
            {
                Console.WriteLine("\n[Console Control] Terminal shutdown detected. Transitioning to detached background mode...");
            }
            catch { }
            
            TransitionToDetachedMode();
            return true;
        }
        return false;
    }

    private static void TransitionToDetachedMode()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _isDetaching, 1, 0) != 0)
        {
            return;
        }

        try
        {
            string? processPath = null;
            string candidateExe = Path.Combine(AppContext.BaseDirectory, "WallpaperTurbo.AppRunner.exe");
            if (File.Exists(candidateExe))
            {
                processPath = candidateExe;
            }
            else
            {
                processPath = Environment.ProcessPath;
            }

            string decodeArgument = _useSoftwareDecode ? " --software-decode" : string.Empty;
            string arguments = $"--wallpaper {_finalWallpaperIndex} --silent --pause-mode {_pauseMode}{decodeArgument}";

            if (string.IsNullOrEmpty(processPath) || 
                processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) || 
                processPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string dllPath = Path.Combine(AppContext.BaseDirectory, "WallpaperTurbo.AppRunner.dll");
                if (File.Exists(dllPath))
                {
                    processPath = Environment.ProcessPath ?? "dotnet";
                    arguments = $"\"{dllPath}\" {arguments}";
                }
            }

            if (!string.IsNullOrEmpty(processPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = processPath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    WorkingDirectory = AppContext.BaseDirectory
                };

                System.Diagnostics.Process.Start(psi);
                System.Threading.Thread.Sleep(500);
            }
        }
        catch (Exception ex)
        {
            try
            {
                Console.Error.WriteLine($"[Detach] Relaunch failed: {ex.Message}");
            }
            catch { }
        }
    }

    private static async Task<int> Main(
        string[] args)
    {
        try
        {
            SetProcessDpiAwarenessContext((IntPtr)(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch
        {
            try
            {
                SetProcessDPIAware();
            }
            catch { }
        }

        using CancellationTokenSource cts =
            new();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        bool isDetach = false;
        bool isStop = false;
        bool isSilent = false;
        int? wallpaperIndex = null;
        PauseMode pauseMode = PauseMode.Maximized;
        bool pauseModeExplicitlySet = false;
        bool useSoftwareDecode = false;
        string? videoOutputModule = null;
        bool memoryDiagnostics = false;
        bool noDetachOnStdinClose = false;
        bool skipDesktopAttach = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--detach", StringComparison.OrdinalIgnoreCase))
            {
                isDetach = true;
            }
            else if (args[i].Equals("--stop", StringComparison.OrdinalIgnoreCase))
            {
                isStop = true;
            }
            else if (args[i].Equals("--silent", StringComparison.OrdinalIgnoreCase))
            {
                isSilent = true;
            }
            else if (args[i].Equals("--software-decode", StringComparison.OrdinalIgnoreCase))
            {
                useSoftwareDecode = true;
            }
            else if (args[i].Equals("--vout", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    videoOutputModule = args[i + 1];
                    i++;
                }
            }
            else if (args[i].StartsWith("--vout=", StringComparison.OrdinalIgnoreCase))
            {
                videoOutputModule = args[i]["--vout=".Length..];
            }
            else if (args[i].Equals("--mem-diagnostics", StringComparison.OrdinalIgnoreCase) ||
                     args[i].Equals("--memory-diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                memoryDiagnostics = true;
            }
            else if (args[i].Equals("--no-detach-on-stdin-close", StringComparison.OrdinalIgnoreCase))
            {
                noDetachOnStdinClose = true;
            }
            else if (args[i].Equals("--skip-desktop-attach", StringComparison.OrdinalIgnoreCase))
            {
                skipDesktopAttach = true;
            }
            else if (args[i].Equals("--wallpaper", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int index))
                {
                    wallpaperIndex = index;
                    i++;
                }
            }
            else if (args[i].Equals("--pause-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && Enum.TryParse<PauseMode>(args[i + 1], true, out var mode))
                {
                    pauseMode = mode;
                    pauseModeExplicitlySet = true;
                    i++;
                }
            }
            else if (args[i].Equals("--pause-on-focus", StringComparison.OrdinalIgnoreCase))
            {
                pauseMode = PauseMode.Focused;
                pauseModeExplicitlySet = true;
            }
            else if (int.TryParse(args[i], out int index))
            {
                wallpaperIndex = index;
            }
        }

        if (isSilent)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "wallpaper.log");
                var fileStream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _logWriter = new StreamWriter(fileStream) { AutoFlush = true };
                Console.SetOut(_logWriter);
                Console.SetError(_logWriter);
                Console.WriteLine($"[Wallpaper Turbo Background Log - Started {DateTime.Now}]");
            }
            catch { }
        }

        if (isStop)
        {
            StopRunningInstances();
            return 0;
        }

        string manifestPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WallpaperManifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Manifest file not found: {manifestPath}");
            return 1;
        }

        WallpaperManifest manifest = WallpaperLibrary.Load(manifestPath);
        int finalWallpaperIndex = 1;

        if (wallpaperIndex.HasValue)
        {
            finalWallpaperIndex = Math.Clamp(wallpaperIndex.Value, 1, manifest.Wallpapers.Count);
        }
        else
        {
            if (isSilent)
            {
                finalWallpaperIndex = 1;
            }
            else
            {
                Console.WriteLine("Available Wallpapers:\n");
                for (int i = 0; i < manifest.Wallpapers.Count; i++)
                {
                    WallpaperEntry item = manifest.Wallpapers[i];
                    Console.WriteLine($"[{i + 1}] {item.Title}  —  {item.Author}");
                }
                Console.Write("\nSelect wallpaper number: ");
                string? input = Console.ReadLine();
                if (!int.TryParse(input, out int selection))
                {
                    selection = 1;
                }
                finalWallpaperIndex = Math.Clamp(selection, 1, manifest.Wallpapers.Count);
            }
        }

        if (!pauseModeExplicitlySet && !isSilent)
        {
            Console.WriteLine("\nSelect Performance Pause Mode:");
            Console.WriteLine("[1] With Focus Mode (pauses when another window is focused)");
            Console.WriteLine("[2] Without Focus Mode (plays continuously, pauses only when maximized/fullscreen)");
            Console.Write("\nSelect option [1 or 2, default 2]: ");
            string? pauseInput = Console.ReadLine();
            if (pauseInput == "1")
            {
                pauseMode = PauseMode.Focused;
            }
            else
            {
                pauseMode = PauseMode.Maximized;
            }
        }

        if (isDetach)
        {
            StopRunningInstances();

            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                processPath = Path.Combine(AppContext.BaseDirectory, "WallpaperTurbo.AppRunner.exe");
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = processPath,
                Arguments = $"--wallpaper {finalWallpaperIndex} --silent --pause-mode {pauseMode}{(useSoftwareDecode ? " --software-decode" : string.Empty)}",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            };

            try
            {
                System.Diagnostics.Process.Start(psi);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nStarted Wallpaper Turbo in the background with wallpaper #{finalWallpaperIndex}.");
                Console.WriteLine("You can safely close this terminal and VS Code now!");
                Console.ResetColor();
                
                // Allow a small delay for the process to launch before parent exits
                await Task.Delay(1500);
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to start background process: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        _finalWallpaperIndex = finalWallpaperIndex;
        _pauseMode = pauseMode;
        _useSoftwareDecode = useSoftwareDecode;
        _videoOutputModule = videoOutputModule;
        _memoryDiagnostics = memoryDiagnostics;
        LogMemory("startup.after-args");

        _consoleCtrlHandler = OnConsoleCtrl;
        SetConsoleCtrlHandler(_consoleCtrlHandler, true);

        ExplorerRestartMonitor? restartMonitor =
            null;

        ForegroundWindowWatcher? foregroundWatcher =
            null;

        CancellationTokenSource? displayChangeCts =
            null;

        try
        {
            if (!isSilent)
            {
                PrintBanner();
            }

            IHardwareDetector detector =
                new WindowsHardwareDetector();

            IEnumerable<GpuInfo> gpus =
                await detector
                    .GetGpusAsync(cts.Token)
                    .ConfigureAwait(false);

            MonitorInfo monitor =
                MonitorManager.GetPrimaryMonitor();

            if (!isSilent)
            {
                PrintTopology(gpus);
            }

            WindowsWallpaperManager wallpaperManager =
                new(Console.WriteLine);

            //
            // CREATE RENDER WINDOW
            //
            _hwnd =
                await NativeRenderWindow
                    .CreateAsync(monitor);
            LogMemory("render-window.created");

            if (_hwnd == IntPtr.Zero)
            {
                Console.WriteLine(
                    "Failed to create render window.");

                return 1;
            }

            if (!isSilent)
            {
                Console.WriteLine(
                    $"\nRender HWND: {PtrToString(_hwnd)}");

                DesktopWindowInspector
                    .DumpShellWindows();
            }

            if (!skipDesktopAttach)
            {
                //
                // ATTACH TO DESKTOP
                //
                bool attached =
                    wallpaperManager.AttachWindow(
                        _hwnd,
                        monitor);

                if (!attached)
                {
                    Console.WriteLine(
                        "Failed to attach wallpaper window to desktop.");

                    return 1;
                }
            }
            else
            {
                Console.WriteLine(
                    "[Diagnostics] Desktop attachment skipped; render window remains top-level.");
            }
            LogMemory("desktop.attached");

            //
            // VERY IMPORTANT:
            // Re-apply fullscreen bounds AFTER parenting.
            //
            int relativeX = monitor.X;
            int relativeY = monitor.Y;
            IntPtr parent = NativeMethods.GetParent(_hwnd);
            if (parent != IntPtr.Zero)
            {
                NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
                NativeMethods.MapWindowPoints(IntPtr.Zero, parent, ref prct, 2);
                relativeX = prct.Left;
                relativeY = prct.Top;
            }

            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                relativeX,
                relativeY,
                monitor.Width,
                monitor.Height,
                (uint)(
                    NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                    NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                    NativeMethods.SetWindowPosFlags.SWP_NOZORDER));

            //
            // INIT MEDIA PIPELINE
            //
            Console.WriteLine(
                "Initializing Hardware Decode Pipeline...");

            _activePipeline =
                new HardwareDecodePipeline(useSoftwareDecode, videoOutputModule);

            LogMemory("pipeline.before-initialize");
            _activePipeline.Initialize(_hwnd);
            LogMemory("pipeline.after-initialize");

            //
            // SELECT WALLPAPER
            //
            WallpaperEntry wallpaper = manifest.Wallpapers[finalWallpaperIndex - 1];

            string videoPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    wallpaper.Video);

            Console.WriteLine(
                $"Loaded wallpaper: {wallpaper.Title}");

            Console.WriteLine(
                $"Author: {wallpaper.Author}");

            Console.WriteLine(
                $"Video Source: {videoPath}\n");

            if (!File.Exists(videoPath))
            {
                ShowMissingWallpaperWarning(
                    videoPath);

                return 1;
            }

            Console.WriteLine(
                $"Loading media stream: {videoPath}");

            LogMemory("media.before-load");
            _activePipeline.LoadMedia(videoPath);
            LogMemory("media.after-load");

            //
            // CREATE SESSION
            //
            _sessionManager =
                new WallpaperSessionManager();

            WallpaperSession session =
                new(
                    _hwnd,
                    wallpaper,
                    _activePipeline,
                    monitor);

            _sessionManager.AddSession(
                session);

            //
            // START PLAYBACK
            //
            LogMemory("playback.before-play");
            session.Play();
            LogMemory("playback.after-play");

            //
            // STABILITY & PERFORMANCE WATCHERS
            //
            bool isRecovering = false;
            restartMonitor = new ExplorerRestartMonitor();
            restartMonitor.RestartDetected += () =>
            {
                if (isRecovering) return;
                isRecovering = true;
                
                Console.WriteLine("[Stability] Explorer restart detected! Re-attaching wallpaper in 2 seconds...");
                
                Task.Run(async () =>
                {
                    try
                    {
                        // Capture references for non-blocking cleanup
                        var oldSessionManager = _sessionManager;
                        var oldHwnd = _hwnd;

                        // Stop all active sessions and destroy the old render window asynchronously in the background to prevent LibVLC/D3D11 deadlocks
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                Console.WriteLine("[Stability] Cleaning up old wallpaper session in the background...");
                                oldSessionManager?.ShutdownAll();
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Stability] Background session shutdown error: {ex.Message}");
                            }

                            try
                            {
                                if (oldHwnd != IntPtr.Zero)
                                {
                                    NativeRenderWindow.Shutdown(oldHwnd);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Stability] Background window shutdown error: {ex.Message}");
                            }
                        });

                        // Instantly create a new session manager for the fresh session
                        _sessionManager = new WallpaperSessionManager();
                        _hwnd = IntPtr.Zero;

                        // Wait up to 10 seconds for Explorer and Desktop shell to fully settle and recreate WorkerW
                        bool success = false;
                        int maxAttempts = 10;
                        MonitorInfo freshMonitor = MonitorManager.GetPrimaryMonitor();

                        for (int attempt = 1; attempt <= maxAttempts; attempt++)
                        {
                            try
                            {
                                Console.WriteLine($"[Stability] Recovery attempt {attempt} of {maxAttempts}...");
                                
                                // Allow Explorer and Desktop shell to settle after restarting
                                await Task.Delay(1000);

                                // Re-verify that Progman and WorkerW exist before proceeding
                                IntPtr progman = DesktopUtil.GetProgman();
                                IntPtr workerW = DesktopUtil.GetDesktopWorkerW();
                                
                                if (progman == IntPtr.Zero || workerW == IntPtr.Zero)
                                {
                                    Console.WriteLine($"[Stability] Desktop shell not fully initialized yet (Progman: {progman != IntPtr.Zero}, WorkerW: {workerW != IntPtr.Zero}). Retrying...");
                                    continue;
                                }

                                // Query monitor again (in case dimensions changed during restart)
                                freshMonitor = MonitorManager.GetPrimaryMonitor();

                                // Recreate the render window
                                if (_hwnd == IntPtr.Zero)
                                {
                                    _hwnd = await NativeRenderWindow.CreateAsync(freshMonitor);
                                }
                                
                                if (_hwnd == IntPtr.Zero)
                                {
                                    Console.Error.WriteLine("[Stability] Failed to recreate render window. Retrying...");
                                    continue;
                                }

                                // Re-attach to the new Explorer desktop shell
                                var freshManager = new WindowsWallpaperManager();
                                bool freshAttached = freshManager.AttachWindow(_hwnd, freshMonitor);
                                if (!freshAttached)
                                {
                                    Console.Error.WriteLine("[Stability] Failed to attach window to desktop. Retrying...");
                                    // Destroy _hwnd so we can recreate it fresh next attempt
                                    NativeRenderWindow.Shutdown(_hwnd);
                                    _hwnd = IntPtr.Zero;
                                    continue;
                                }

                                success = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Stability] Exception during recovery attempt {attempt}: {ex.Message}");
                                if (_hwnd != IntPtr.Zero)
                                {
                                    NativeRenderWindow.Shutdown(_hwnd);
                                    _hwnd = IntPtr.Zero;
                                }
                            }
                        }

                        if (!success)
                        {
                            Console.Error.WriteLine("[Stability] Explorer restart recovery failed after maximum retries.");
                            return;
                        }

                        // Map positions and apply SetWindowPos relative to new parent
                        int relX = freshMonitor.X;
                        int relY = freshMonitor.Y;
                        IntPtr prnt = NativeMethods.GetParent(_hwnd);
                        if (prnt != IntPtr.Zero)
                        {
                            NativeMethods.RECT prct = new NativeMethods.RECT 
                            { 
                                Left = freshMonitor.X, 
                                Top = freshMonitor.Y, 
                                Right = freshMonitor.X + freshMonitor.Width, 
                                Bottom = freshMonitor.Y + freshMonitor.Height 
                            };
                            NativeMethods.MapWindowPoints(IntPtr.Zero, prnt, ref prct, 2);
                            relX = prct.Left;
                            relY = prct.Top;
                        }

                        NativeMethods.SetWindowPos(
                            _hwnd,
                            IntPtr.Zero,
                            relX,
                            relY,
                            freshMonitor.Width,
                            freshMonitor.Height,
                            (uint)(
                                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                                NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                                NativeMethods.SetWindowPosFlags.SWP_NOZORDER));

                        // Initialize the media pipeline on the new handle
                        var freshPipeline = new HardwareDecodePipeline(useSoftwareDecode, videoOutputModule);
                        freshPipeline.Initialize(_hwnd);

                        var freshWallpaper = manifest.Wallpapers[finalWallpaperIndex - 1];
                        string freshVideoPath = Path.Combine(AppContext.BaseDirectory, freshWallpaper.Video);
                        if (File.Exists(freshVideoPath))
                        {
                            freshPipeline.LoadMedia(freshVideoPath);
                            
                            var freshSession = new WallpaperSession(
                                _hwnd, 
                                freshWallpaper, 
                                freshPipeline, 
                                freshMonitor);
                            
                            _sessionManager?.AddSession(freshSession);
                            freshSession.Play();
                            
                            // Reassign pipeline to outer static variable for safety/cleanup
                            _activePipeline = freshPipeline;

                            await Task.Delay(500);
                            WindowUtil.MakeChildrenTransparent(_hwnd);

                            // Re-enforce z-order persistence exactly as in Main
                            if (DesktopUtil.IsRaisedDesktop())
                            {
                                IntPtr shellView = DesktopUtil.GetDesktopShellView();
                                IntPtr progman = DesktopUtil.GetProgman();
                                IntPtr workerW = DesktopUtil.GetDesktopWorkerW();

                                WallpaperTurbo.Core.Rendering.NativeRenderWindow.ShellViewHandle = shellView;

                                if (shellView != IntPtr.Zero)
                                {
                                    NativeMethods.SetWindowPos(
                                        _hwnd,
                                        shellView,
                                        relX,
                                        relY,
                                        freshMonitor.Width,
                                        freshMonitor.Height,
                                        (uint)(
                                            NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                                            NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));
                                }
                                else
                                {
                                    NativeMethods.SetWindowPos(
                                        _hwnd,
                                        IntPtr.Zero,
                                        relX,
                                        relY,
                                        freshMonitor.Width,
                                        freshMonitor.Height,
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
                                WindowUtil.SendToBottom(_hwnd);

                                NativeMethods.SetWindowPos(
                                    _hwnd,
                                    NativeMethods.HWND_BOTTOM,
                                    relX,
                                    relY,
                                    freshMonitor.Width,
                                    freshMonitor.Height,
                                    (uint)(
                                        NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                                        NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                                        NativeMethods.SetWindowPosFlags.SWP_NOOWNERZORDER |
                                        NativeMethods.SetWindowPosFlags.SWP_NOSENDCHANGING));
                            }

                            Console.WriteLine("[Stability] Re-attachment successful!");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Stability] Error during Explorer restart recovery: {ex.Message}");
                    }
                    finally
                    {
                        isRecovering = false;
                    }
                });
            };

            object displayChangeLock = new();

            restartMonitor.DisplaySettingsChanged += () =>
            {
                CancellationToken token;
                lock (displayChangeLock)
                {
                    displayChangeCts?.Cancel();
                    displayChangeCts?.Dispose();
                    displayChangeCts = new CancellationTokenSource();
                    token = displayChangeCts.Token;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        Console.WriteLine("[Stability] Display settings change detected! Scheduling debounced layout re-adjustment...");
                        
                        // Coalesced debounce delay (500ms) to let monitor/DWM layers fully settle
                        await Task.Delay(500, token);

                        if (_hwnd == IntPtr.Zero)
                            return;

                        // Query all screens currently active (automatically handles multi-monitor, disconnects, reorders)
                        var freshMonitors = MonitorManager.GetMonitors();

                        if (_sessionManager != null)
                        {
                            foreach (var session in _sessionManager.Sessions)
                            {
                                // Find the matching monitor in the updated list by DeviceName (fallback to updated primary monitor)
                                MonitorInfo? freshMonitor = null;
                                foreach (var m in freshMonitors)
                                {
                                    if (m.DeviceName == session.Monitor.DeviceName)
                                    {
                                        freshMonitor = m;
                                        break;
                                    }
                                }

                                if (freshMonitor == null)
                                {
                                    // Fallback: target screen disconnected, fall back to updated primary monitor
                                    freshMonitor = MonitorManager.GetPrimaryMonitor();
                                }

                                Console.WriteLine($"[Stability] Re-evaluating screen layout for monitor '{freshMonitor.DeviceName}'. Bounds: {freshMonitor.Width}x{freshMonitor.Height} at ({freshMonitor.X},{freshMonitor.Y})");

                                // Re-parent mapping coordinates
                                int relX = freshMonitor.X;
                                int relY = freshMonitor.Y;
                                IntPtr prnt = NativeMethods.GetParent(session.WindowHandle);
                                if (prnt != IntPtr.Zero)
                                {
                                    NativeMethods.RECT prct = new NativeMethods.RECT 
                                    { 
                                        Left = freshMonitor.X, 
                                        Top = freshMonitor.Y, 
                                        Right = freshMonitor.X + freshMonitor.Width, 
                                        Bottom = freshMonitor.Y + freshMonitor.Height 
                                    };
                                    NativeMethods.MapWindowPoints(IntPtr.Zero, prnt, ref prct, 2);
                                    relX = prct.Left;
                                    relY = prct.Top;
                                }

                                // Resize the render window. Critical: Use SWP_NOZORDER | SWP_NOACTIVATE to preserve desktop icon layering!
                                NativeMethods.SetWindowPos(
                                    session.WindowHandle,
                                    IntPtr.Zero,
                                    relX,
                                    relY,
                                    freshMonitor.Width,
                                    freshMonitor.Height,
                                    (uint)(
                                        NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                                        NativeMethods.SetWindowPosFlags.SWP_NOZORDER |
                                        NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));

                                // Update the session's internal monitor topology reference
                                session.UpdateMonitor(freshMonitor);

                                // Dynamic re-apply layout modes using fresh bounds to flush and recalculate LibVLC scaling
                                session.MediaPipeline.ApplyLayoutMode(session.Wallpaper.GetLayoutMode());
                            }
                        }

                        Console.WriteLine("[Stability] Display re-layout complete!");
                    }
                    catch (OperationCanceledException)
                    {
                        // Safely ignored, coalesced by a subsequent display change
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Stability] Error adjusting layout to display changes: {ex.Message}");
                    }
                });
            };

            foregroundWatcher = new ForegroundWindowWatcher(pauseMode);
            Console.WriteLine($"[Performance] Active Performance Pause Mode: {pauseMode}");
            LogMemory("watchers.started");
            foregroundWatcher.VisibilityChanged += (isObscured) =>
            {
                if (isObscured)
                {
                    string reason = pauseMode == PauseMode.Focused 
                        ? "application focused" 
                        : "fullscreen/maximized window";
                    Console.WriteLine($"[Performance] Desktop obscured by {reason}. Suspending playback...");
                    if (_sessionManager != null)
                    {
                        foreach (var s in _sessionManager.Sessions)
                        {
                            s.Pause();
                        }
                    }
                    LogMemory("performance.paused");
                }
                else
                {
                    Console.WriteLine("[Performance] Desktop is now visible. Resuming playback...");
                    if (_sessionManager != null)
                    {
                        foreach (var s in _sessionManager.Sessions)
                        {
                            s.Play();
                        }
                    }
                    LogMemory("performance.resumed");
                }
            };

            //
            // VERY IMPORTANT:
            // Allow VLC/DWM to create child surfaces first.
            //
            await Task.Delay(500);

            // Make all dynamically spawned media player child windows click-through
            LogMemory("vlc-children.before-style");
            WindowUtil.MakeChildrenTransparent(_hwnd);
            LogMemory("vlc-children.after-style");

            //
            // Re-enforce z-order AFTER playback starts.
            //
            if (DesktopUtil.IsRaisedDesktop())
            {
                IntPtr shellView = DesktopUtil.GetDesktopShellView();
                IntPtr progman = DesktopUtil.GetProgman();
                IntPtr workerW = DesktopUtil.GetDesktopWorkerW();

                // Re-enforce ShellViewHandle for WndProc Z-order lock
                WallpaperTurbo.Core.Rendering.NativeRenderWindow.ShellViewHandle = shellView;

                int finalX = monitor.X;
                int finalY = monitor.Y;
                IntPtr finalParent = NativeMethods.GetParent(_hwnd);
                if (finalParent != IntPtr.Zero)
                {
                    NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
                    NativeMethods.MapWindowPoints(IntPtr.Zero, finalParent, ref prct, 2);
                    finalX = prct.Left;
                    finalY = prct.Top;
                }

                if (shellView != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(
                        _hwnd,
                        shellView,
                        finalX,
                        finalY,
                        monitor.Width,
                        monitor.Height,
                        (uint)(
                            NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                            NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));
                }
                else
                {
                    NativeMethods.SetWindowPos(
                        _hwnd,
                        IntPtr.Zero,
                        finalX,
                        finalY,
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
                WindowUtil.SendToBottom(_hwnd);

                int finalX = monitor.X;
                int finalY = monitor.Y;
                IntPtr finalParent = NativeMethods.GetParent(_hwnd);
                if (finalParent != IntPtr.Zero)
                {
                    NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
                    NativeMethods.MapWindowPoints(IntPtr.Zero, finalParent, ref prct, 2);
                    finalX = prct.Left;
                    finalY = prct.Top;
                }

                NativeMethods.SetWindowPos(
                    _hwnd,
                    NativeMethods.HWND_BOTTOM,
                    finalX,
                    finalY,
                    monitor.Width,
                    monitor.Height,
                    (uint)(
                        NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                        NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                        NativeMethods.SetWindowPosFlags.SWP_NOOWNERZORDER |
                        NativeMethods.SetWindowPosFlags.SWP_NOSENDCHANGING));
            }
            LogMemory("desktop.zorder-enforced");

            if (isSilent)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nWallpaper Turbo is running in interactive mode!");
                Console.WriteLine("Available commands:");
                Console.WriteLine("  swap <index>  - Change wallpaper to manifest index (e.g., swap 2)");
                Console.WriteLine("  pause         - Pause active wallpaper playback");
                Console.WriteLine("  play          - Resume active wallpaper playback");
                Console.WriteLine("  layout <mode> - Change layout (stretch, fit, fill)");
                Console.WriteLine("  mem           - Print current memory diagnostics");
                Console.WriteLine("  exit          - Exit Wallpaper Turbo cleanly");
                Console.ResetColor();

                while (!cts.Token.IsCancellationRequested)
                {
                    Console.Write("\nTurboClient> ");
                    string? line = Console.ReadLine();
                    if (line == null)
                    {
                        try
                        {
                            Console.WriteLine(noDetachOnStdinClose
                                ? "\n[Console Closed] Standard input closed. Shutting down because detach is disabled for this run..."
                                : "\n[Console Closed] Standard input closed. Transitioning to detached background mode...");
                        }
                        catch { }

                        if (!noDetachOnStdinClose)
                        {
                            TransitionToDetachedMode();
                        }

                        break;
                    }

                    line = line.Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    await ProcessCommandAsync(line, manifest, _sessionManager, _hwnd, cts.Token);
                }
            }

            Console.WriteLine(
                "\nStopping wallpaper playback...");

            Console.WriteLine(
                "Releasing media pipeline...");

            _sessionManager?.ShutdownAll();
            _activePipeline = null;

            Console.WriteLine(
                "Shutting down render window...");

            NativeRenderWindow.Shutdown(_hwnd);
            _hwnd = IntPtr.Zero;

            Console.WriteLine(
                "Wallpaper Turbo shutdown complete.");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(
                "Operation cancelled.");

            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Fatal engine error: {ex}");

            return 1;
        }
        finally
        {
            try
            {
                displayChangeCts?.Cancel();
                displayChangeCts?.Dispose();
            }
            catch
            {
            }

            try
            {
                restartMonitor?.Dispose();
            }
            catch
            {
            }

            try
            {
                foregroundWatcher?.Dispose();
            }
            catch
            {
            }

            try
            {
                _activePipeline?.Release();
            }
            catch
            {
            }

            try
            {
                if (_hwnd != IntPtr.Zero)
                {
                    NativeRenderWindow.Shutdown(_hwnd);
                }
            }
            catch
            {
            }

            _sessionManager?.Dispose();
        }
    }

    private static async Task ProcessCommandAsync(
        string commandLine,
        WallpaperManifest manifest,
        WallpaperSessionManager? sessionManager,
        IntPtr hwnd,
        CancellationToken cancellationToken)
    {
        var parts = commandLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string command = parts[0].ToLowerInvariant();
        string args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (command)
        {
            case "swap":
                if (int.TryParse(args, out int newIndex))
                {
                    await HandleSwapCommandAsync(newIndex, manifest, sessionManager, hwnd);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Please specify a valid wallpaper index. Example: swap 2");
                    Console.ResetColor();
                }
                break;

            case "pause":
                if (sessionManager != null)
                {
                    foreach (var s in sessionManager.Sessions)
                    {
                        s.Pause();
                    }
                    Console.WriteLine("Playback paused.");
                }
                break;

            case "play":
                if (sessionManager != null)
                {
                    foreach (var s in sessionManager.Sessions)
                    {
                        s.Play();
                    }
                    Console.WriteLine("Playback resumed.");
                }
                break;

            case "mem":
                LogMemory("console.mem", force: true);
                break;

            case "layout":
                if (Enum.TryParse<WallpaperLayoutMode>(args, true, out var mode))
                {
                    if (sessionManager != null)
                    {
                        foreach (var s in sessionManager.Sessions)
                        {
                            s.MediaPipeline.ApplyLayoutMode(mode);
                        }
                        Console.WriteLine($"Layout mode updated to: {mode}");
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Invalid layout mode. Use: stretch, fit, or fill");
                    Console.ResetColor();
                }
                break;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unknown command: {command}");
                Console.ResetColor();
                break;
        }
    }

    private static async Task HandleSwapCommandAsync(
        int newIndex,
        WallpaperManifest manifest,
        WallpaperSessionManager? sessionManager,
        IntPtr hwnd)
    {
        if (sessionManager == null || hwnd == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Session manager or window handle not initialized.");
            Console.ResetColor();
            return;
        }

        if (newIndex < 1 || newIndex > manifest.Wallpapers.Count)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Invalid wallpaper index. Must be between 1 and {manifest.Wallpapers.Count}.");
            Console.ResetColor();
            return;
        }

        WallpaperEntry newWallpaper = manifest.Wallpapers[newIndex - 1];
        string videoPath = Path.Combine(AppContext.BaseDirectory, newWallpaper.Video);

        if (!File.Exists(videoPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Video file not found: {videoPath}");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"\n[HotSwap] Initiating swap to wallpaper #{newIndex}: '{newWallpaper.Title}'...");

        var sessions = sessionManager.Sessions;
        if (sessions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No active wallpaper sessions found.");
            Console.ResetColor();
            return;
        }

        var oldSession = sessions[0];
        var activePipeline = oldSession.MediaPipeline;

        await Task.Run(async () =>
        {
            try
            {
                // 1. Gracefully pause old session first
                try
                {
                    oldSession.Pause();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HotSwap] Warning during pause: {ex.Message}");
                }

                activePipeline.LoadMedia(videoPath);

                var newSession = new WallpaperSession(
                    hwnd,
                    newWallpaper,
                    activePipeline,
                    oldSession.Monitor);

                sessionManager.ReplaceSession(oldSession, newSession);

                _activePipeline = activePipeline;
                _finalWallpaperIndex = newIndex;

                newSession.Play();

                await Task.Delay(500);
                WindowUtil.MakeChildrenTransparent(hwnd);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[HotSwap] Successfully hot-swapped to '{newWallpaper.Title}'!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[HotSwap] Error: Failed to complete hot-swap: {ex.Message}");
                Console.ResetColor();
            }
        });
    }

    private static void ShowMissingWallpaperWarning(
        string videoPath)
    {
        Console.ForegroundColor =
            ConsoleColor.Yellow;

        Console.WriteLine(
            "\n==========================================================");

        Console.WriteLine(
            "WARNING: Video file not found!");

        Console.WriteLine(
            $"Current target: {videoPath}");

        Console.WriteLine(
            "==========================================================\n");

        Console.ResetColor();
    }

    private static void PrintBanner()
    {
        Console.Title =
            "Wallpaper Turbo";
    }

    private static void PrintTopology(
        IEnumerable<GpuInfo> gpus)
    {
        Console.WriteLine(
            "=== Wallpaper Turbo — Detected GPU Topology ===\n");

        List<GpuInfo> list =
            new(gpus);

        if (list.Count == 0)
        {
            Console.WriteLine(
                "No GPUs detected.");

            return;
        }

        for (int i = 0;
             i < list.Count;
             i++)
        {
            GpuInfo gpu =
                list[i];

            Console.WriteLine(
                $"GPU #{i + 1}: {gpu.Name}");

            Console.WriteLine(
                $"  Vendor     : {gpu.Vendor}");

            Console.WriteLine(
                $"  Dedicated  : {gpu.IsDedicated}");

            Console.WriteLine(
                $"  VRAM       : {FormatBytes(gpu.VramBytes)}");

            Console.WriteLine();
        }

        MonitorInfo monitor =
            MonitorManager.GetPrimaryMonitor();

        Console.WriteLine(
            $"{monitor.DeviceName} | {monitor.Width}x{monitor.Height} | Primary: {monitor.IsPrimary}");
    }

    private static string FormatBytes(
        ulong bytes)
    {
        if (bytes == 0)
            return "Unknown";

        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        double value =
            bytes;

        int unit =
            0;

        while (value >= 1024 &&
               unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(
            "{0:0.##} {1}",
            value,
            units[unit]);
    }

    private static string FormatBytes(
        long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        double value =
            bytes;

        int unit =
            0;

        while (value >= 1024 &&
               unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(
            "{0:0.##} {1}",
            value,
            units[unit]);
    }

    private static void LogMemory(
        string checkpoint,
        bool force = false)
    {
        if (!_memoryDiagnostics && !force)
            return;

        try
        {
            using var process =
                System.Diagnostics.Process.GetCurrentProcess();

            process.Refresh();

            GCMemoryInfo gcInfo =
                GC.GetGCMemoryInfo();

            Console.WriteLine(
                $"[Memory:{checkpoint}] " +
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
            Console.WriteLine(
                $"[Memory:{checkpoint}] Failed to read process memory: {ex.Message}");
        }
    }

    private static string PtrToString(
        IntPtr p)
    {
        if (p == IntPtr.Zero)
            return "0x0";

        return IntPtr.Size == 8
            ? $"0x{p.ToInt64():X}"
            : $"0x{p.ToInt32():X}";
    }

    private static void StopRunningInstances()
    {
        int currentId = System.Diagnostics.Process.GetCurrentProcess().Id;
        string currentName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        string[] candidateNames = currentName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? ["WallpaperTurbo.AppRunner"]
            : [currentName, "WallpaperTurbo.AppRunner"];

        try
        {
            int stoppedCount = 0;
            foreach (string processName in candidateNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    if (process.Id != currentId)
                    {
                        try
                        {
                            process.Kill(true); // Kill process and its children recursively
                            process.WaitForExit(3000);
                            stoppedCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to stop process {process.Id}: {ex.Message}");
                        }
                    }
                }
            }
            if (stoppedCount > 0)
            {
                Console.WriteLine($"Stopped {stoppedCount} running instance(s) of Wallpaper Turbo.");
            }
            else
            {
                Console.WriteLine("No other running instances of Wallpaper Turbo found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating processes: {ex.Message}");
        }
    }
}
