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

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

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
                Arguments = $"--wallpaper {finalWallpaperIndex} --silent --pause-mode {pauseMode}",
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

        IntPtr hwnd =
            IntPtr.Zero;

        IMediaPipeline? pipeline =
            null;

        WallpaperSessionManager? sessionManager =
            null;

        ExplorerRestartMonitor? restartMonitor =
            null;

        ForegroundWindowWatcher? foregroundWatcher =
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
            hwnd =
                await NativeRenderWindow
                    .CreateAsync(monitor);

            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine(
                    "Failed to create render window.");

                return 1;
            }

            if (!isSilent)
            {
                Console.WriteLine(
                    $"\nRender HWND: {PtrToString(hwnd)}");

                DesktopWindowInspector
                    .DumpShellWindows();
            }

            //
            // ATTACH TO DESKTOP
            //
            bool attached =
                wallpaperManager.AttachWindow(
                    hwnd,
                    monitor);

            if (!attached)
            {
                Console.WriteLine(
                    "Failed to attach wallpaper window to desktop.");

                return 1;
            }

            //
            // VERY IMPORTANT:
            // Re-apply fullscreen bounds AFTER parenting.
            //
            int relativeX = monitor.X;
            int relativeY = monitor.Y;
            IntPtr parent = NativeMethods.GetParent(hwnd);
            if (parent != IntPtr.Zero)
            {
                NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
                NativeMethods.MapWindowPoints(IntPtr.Zero, parent, ref prct, 2);
                relativeX = prct.Left;
                relativeY = prct.Top;
            }

            NativeMethods.SetWindowPos(
                hwnd,
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

            pipeline =
                new HardwareDecodePipeline();

            pipeline.Initialize(hwnd);

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

            pipeline.LoadMedia(videoPath);

            //
            // CREATE SESSION
            //
            sessionManager =
                new WallpaperSessionManager();

            WallpaperSession session =
                new(
                    hwnd,
                    wallpaper,
                    pipeline,
                    monitor);

            sessionManager.AddSession(
                session);

            //
            // START PLAYBACK
            //
            session.Play();

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
                        var oldSessionManager = sessionManager;
                        var oldHwnd = hwnd;

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
                        sessionManager = new WallpaperSessionManager();
                        hwnd = IntPtr.Zero;

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
                                if (hwnd == IntPtr.Zero)
                                {
                                    hwnd = await NativeRenderWindow.CreateAsync(freshMonitor);
                                }
                                
                                if (hwnd == IntPtr.Zero)
                                {
                                    Console.Error.WriteLine("[Stability] Failed to recreate render window. Retrying...");
                                    continue;
                                }

                                // Re-attach to the new Explorer desktop shell
                                var freshManager = new WindowsWallpaperManager();
                                bool freshAttached = freshManager.AttachWindow(hwnd, freshMonitor);
                                if (!freshAttached)
                                {
                                    Console.Error.WriteLine("[Stability] Failed to attach window to desktop. Retrying...");
                                    // Destroy hwnd so we can recreate it fresh next attempt
                                    NativeRenderWindow.Shutdown(hwnd);
                                    hwnd = IntPtr.Zero;
                                    continue;
                                }

                                success = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Stability] Exception during recovery attempt {attempt}: {ex.Message}");
                                if (hwnd != IntPtr.Zero)
                                {
                                    NativeRenderWindow.Shutdown(hwnd);
                                    hwnd = IntPtr.Zero;
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
                        IntPtr prnt = NativeMethods.GetParent(hwnd);
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
                            hwnd,
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
                        var freshPipeline = new HardwareDecodePipeline();
                        freshPipeline.Initialize(hwnd);

                        var freshWallpaper = manifest.Wallpapers[finalWallpaperIndex - 1];
                        string freshVideoPath = Path.Combine(AppContext.BaseDirectory, freshWallpaper.Video);
                        if (File.Exists(freshVideoPath))
                        {
                            freshPipeline.LoadMedia(freshVideoPath);
                            
                            var freshSession = new WallpaperSession(
                                hwnd, 
                                freshWallpaper, 
                                freshPipeline, 
                                freshMonitor);
                            
                            sessionManager?.AddSession(freshSession);
                            freshSession.Play();
                            
                            // Reassign pipeline to outer local variable for safety/cleanup
                            pipeline = freshPipeline;

                            await Task.Delay(500);
                            WindowUtil.MakeChildrenTransparent(hwnd);

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
                                        hwnd,
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
                                        hwnd,
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
                                WindowUtil.SendToBottom(hwnd);

                                NativeMethods.SetWindowPos(
                                    hwnd,
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

            foregroundWatcher = new ForegroundWindowWatcher(pauseMode);
            Console.WriteLine($"[Performance] Active Performance Pause Mode: {pauseMode}");
            foregroundWatcher.VisibilityChanged += (isObscured) =>
            {
                if (isObscured)
                {
                    string reason = pauseMode == PauseMode.Focused 
                        ? "application focused" 
                        : "fullscreen/maximized window";
                    Console.WriteLine($"[Performance] Desktop obscured by {reason}. Suspending playback...");
                    if (sessionManager != null)
                    {
                        foreach (var s in sessionManager.Sessions)
                        {
                            s.Pause();
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[Performance] Desktop is now visible. Resuming playback...");
                    if (sessionManager != null)
                    {
                        foreach (var s in sessionManager.Sessions)
                        {
                            s.Play();
                        }
                    }
                }
            };

            //
            // VERY IMPORTANT:
            // Allow VLC/DWM to create child surfaces first.
            //
            await Task.Delay(500);

            // Make all dynamically spawned media player child windows click-through
            WindowUtil.MakeChildrenTransparent(hwnd);

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
                IntPtr finalParent = NativeMethods.GetParent(hwnd);
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
                        hwnd,
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
                        hwnd,
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
                WindowUtil.SendToBottom(hwnd);

                int finalX = monitor.X;
                int finalY = monitor.Y;
                IntPtr finalParent = NativeMethods.GetParent(hwnd);
                if (finalParent != IntPtr.Zero)
                {
                    NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
                    NativeMethods.MapWindowPoints(IntPtr.Zero, finalParent, ref prct, 2);
                    finalX = prct.Left;
                    finalY = prct.Top;
                }

                NativeMethods.SetWindowPos(
                    hwnd,
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
                Console.ForegroundColor =
                    ConsoleColor.Green;

                Console.WriteLine(
                    "\nWallpaper Turbo is running! Press [ENTER] to exit cleanly.");

                Console.ResetColor();

                Console.ReadLine();
            }

            Console.WriteLine(
                "\nStopping wallpaper playback...");

            Console.WriteLine(
                "Releasing media pipeline...");

            pipeline.Release();

            Console.WriteLine(
                "Shutting down render window...");

            NativeRenderWindow.Shutdown(hwnd);

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
                pipeline?.Release();
            }
            catch
            {
            }

            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    NativeRenderWindow.Shutdown(hwnd);
                }
            }
            catch
            {
            }

            sessionManager?.Dispose();
        }
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

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(currentName);
            int stoppedCount = 0;
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