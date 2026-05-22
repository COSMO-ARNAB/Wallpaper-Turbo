// Program.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Hardware.Models;
using WallpaperTurbo.Core.Interop;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Models;
using WallpaperTurbo.Core.Wallpaper;
using WallpaperTurbo.Core.Services.Performance;
using WallpaperTurbo.Core.Services.IPC;

namespace WallpaperTurbo.AppRunner;

internal static class Program
{
    private static System.IO.StreamWriter? _logWriter;
    private static int _finalWallpaperIndex = 1;
    private static WallpaperEngineOrchestrator? _orchestrator;
    private static int _isDetaching = 0;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    private delegate bool ConsoleCtrlDelegate(int ctrlType);

    private static ConsoleCtrlDelegate? _consoleCtrlHandler;

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

    private static void TransitionToDetachedMode(int? wallpaperIndex = null, string? customVideoPath = null)
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

            string arguments = "--background";
            if (customVideoPath != null)
            {
                arguments += $" --video \"{customVideoPath}\"";
            }
            else
            {
                arguments += $" --wallpaper {wallpaperIndex ?? _finalWallpaperIndex}";
            }

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

    private static async Task<int> HandleCompactCliCommandAsync(string[] args, WallpaperConfig config)
    {
        string cmd = args[0].ToLowerInvariant();
        string cmdArgs = args.Length > 1 ? args[1].Trim() : string.Empty;

        if (cmd == "stop")
        {
            Console.WriteLine("Sending stop command to background service...");
            var response = await WallpaperIpcService.SendCommandAsync(new IpcCommand("Stop"));
            if (response != null)
            {
                Console.WriteLine($"Service: {response.Message}");
            }
            else
            {
                Console.WriteLine("No running service detected via IPC. Performing process cleanup...");
            }

            StopRunningInstances();
            return 0;
        }

        if (cmd == "pause" || cmd == "resume")
        {
            string action = cmd == "pause" ? "Pause" : "Resume";
            var response = await WallpaperIpcService.SendCommandAsync(new IpcCommand(action));
            if (response != null)
            {
                Console.WriteLine($"Service: {response.Message}");
                return 0;
            }
            else
            {
                Console.Error.WriteLine("Error: No active background Wallpaper Turbo service found.");
                return 1;
            }
        }

        if (cmd == "play")
        {
            int? index = null;
            if (int.TryParse(cmdArgs, out int parsedIndex))
            {
                index = parsedIndex;
            }

            // 1. Try sending command to active background process
            var response = await WallpaperIpcService.SendCommandAsync(new IpcCommand("Play", WallpaperIndex: index));
            if (response != null)
            {
                Console.WriteLine($"Service: {response.Message}");
                return 0;
            }

            // 2. No active service running, boot one!
            Console.WriteLine("No active background service found. Starting a new background service...");
            TransitionToDetachedMode(wallpaperIndex: index ?? config.DefaultWallpaperIndex);
            return 0;
        }

        if (cmd == "set")
        {
            if (string.IsNullOrEmpty(cmdArgs))
            {
                Console.Error.WriteLine("Error: Please specify a video file path. Example: set my_video.mp4");
                return 1;
            }

            string resolvedPath = cmdArgs;
            if (!Path.IsPathRooted(resolvedPath))
            {
                resolvedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, resolvedPath));
            }

            if (!File.Exists(resolvedPath))
            {
                Console.Error.WriteLine($"Error: Video file not found: {resolvedPath}");
                return 1;
            }

            // 1. Try sending command to active background process
            var response = await WallpaperIpcService.SendCommandAsync(new IpcCommand("Set", VideoPath: resolvedPath));
            if (response != null)
            {
                Console.WriteLine($"Service: {response.Message}");
                return 0;
            }

            // 2. No active service running, boot one!
            Console.WriteLine("No active background service found. Starting a new background service with custom video...");
            TransitionToDetachedMode(wallpaperIndex: null, customVideoPath: resolvedPath);
            return 0;
        }

        return 0;
    }

    private static async Task<int> Main(string[] args)
    {
        DisablePowerThrottling();

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

        using CancellationTokenSource cts = new();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // Load configuration and parse CLI command paths
        string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        WallpaperConfig config = WallpaperConfig.Load(configPath);

        if (args.Length > 0 && !args[0].StartsWith("-"))
        {
            string cmd = args[0].ToLowerInvariant();
            if (cmd == "play" || cmd == "set" || cmd == "pause" || cmd == "resume" || cmd == "stop")
            {
                return await HandleCompactCliCommandAsync(args, config);
            }
        }

        bool isDetach = false;
        bool isStop = false;
        bool isSilent = false;
        bool isBackground = false;
        string? customVideoPath = null;
        int? wallpaperIndex = null;
        PauseMode pauseMode = config.PauseMode;
        bool pauseModeExplicitlySet = false;
        VideoDecodeMode decodeMode = config.DecodeMode;
        string? videoOutputModule = config.VideoOutputModule;
        bool memoryDiagnostics = config.MemoryDiagnostics;
        bool noDetachOnStdinClose = false;
        bool skipDesktopAttach = false;
        string suspendMode = config.SuspendMode;
        int fileCachingMs = config.FileCachingMs;

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
            else if (args[i].Equals("--background", StringComparison.OrdinalIgnoreCase))
            {
                isBackground = true;
            }
            else if (args[i].Equals("--video", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    customVideoPath = args[i + 1];
                    i++;
                }
            }
            else if (args[i].Equals("--software-decode", StringComparison.OrdinalIgnoreCase))
            {
                decodeMode = VideoDecodeMode.Software;
            }
            else if (args[i].Equals("--hardware-decode", StringComparison.OrdinalIgnoreCase))
            {
                decodeMode = VideoDecodeMode.Hardware;
            }
            else if (args[i].Equals("--decode-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && Enum.TryParse<VideoDecodeMode>(args[i + 1], true, out var mode))
                {
                    decodeMode = mode;
                    i++;
                }
            }
            else if (args[i].StartsWith("--decode-mode=", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<VideoDecodeMode>(args[i]["--decode-mode=".Length..], true, out var mode))
                {
                    decodeMode = mode;
                }
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
            else if (args[i].Equals("--suspend-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    suspendMode = args[i + 1].ToLowerInvariant();
                    i++;
                }
            }
            else if (args[i].StartsWith("--suspend-mode=", StringComparison.OrdinalIgnoreCase))
            {
                suspendMode = args[i]["--suspend-mode=".Length..].ToLowerInvariant();
            }
            else if (args[i].Equals("--file-caching", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int ms))
                {
                    fileCachingMs = ms;
                    i++;
                }
            }
            else if (args[i].StartsWith("--file-caching=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args[i]["--file-caching=".Length..], out int ms))
                {
                    fileCachingMs = ms;
                }
            }
            else if (int.TryParse(args[i], out int index))
            {
                wallpaperIndex = index;
            }
        }

        if (isBackground)
        {
            isSilent = true;
            noDetachOnStdinClose = true;
        }

        // Single-Instance Mutex validation: Prevent duplicate authoritative background hosts
        using var mutex = new Mutex(true, "Global\\WallpaperTurbo_SingleInstanceMutex", out bool createdNew);
        if (isSilent && !createdNew)
        {
            try { Console.Error.WriteLine("[Host] Aborting: Another instance of Wallpaper Turbo is already running."); } catch { }
            return 0;
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

            string decodeArg = decodeMode switch
            {
                VideoDecodeMode.Software => " --software-decode",
                VideoDecodeMode.Hardware => " --hardware-decode",
                _ => string.Empty
            };

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = processPath,
                Arguments = $"--wallpaper {finalWallpaperIndex} --silent --pause-mode {pauseMode}{decodeArg} --suspend-mode {suspendMode} --file-caching {fileCachingMs}",
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

        _consoleCtrlHandler = OnConsoleCtrl;
        SetConsoleCtrlHandler(_consoleCtrlHandler, true);

        try
        {
            if (!isSilent)
            {
                PrintBanner();
            }

            IEnumerable<GpuInfo> gpus = Array.Empty<GpuInfo>();
            if (!isSilent)
            {
                IHardwareDetector detector = new WindowsHardwareDetector();
                gpus = await detector.GetGpusAsync(cts.Token).ConfigureAwait(false);
                PrintTopology(gpus);
            }

            // Instantiate the centralized orchestrator
            _orchestrator = new WallpaperEngineOrchestrator(
                manifest,
                pauseMode,
                decodeMode,
                videoOutputModule,
                memoryDiagnostics,
                suspendMode,
                fileCachingMs,
                skipDesktopAttach,
                isSilent
            );

            // Initialize and play
            bool orchestratorStarted = await _orchestrator.InitializeAndStartAsync(
                finalWallpaperIndex, 
                customVideoPath, 
                cts.Token
            ).ConfigureAwait(false);

            if (!orchestratorStarted)
            {
                Console.Error.WriteLine("[Host] Error: Orchestrator failed to initialize render flow.");
                return 1;
            }

            // Start Named Pipe IPC Server (if authoritative mutex owner)
            if (createdNew)
            {
                Console.WriteLine("[IPC] Starting authoritative background IPC server...");
                WallpaperIpcService.StartServer(async (cmd) =>
                {
                    if (_orchestrator == null)
                    {
                        return new IpcResponse(false, "Error: Engine orchestrator not initialized.");
                    }

                    Console.WriteLine($"[IPC Server] Command received: {cmd.Action} (Index: {cmd.WallpaperIndex}, Path: {cmd.VideoPath})");
                    try
                    {
                        switch (cmd.Action)
                        {
                            case "Play":
                                if (cmd.WallpaperIndex.HasValue)
                                {
                                    bool success = await _orchestrator.SwapWallpaperAsync(cmd.WallpaperIndex.Value);
                                    if (success)
                                    {
                                        int idx = cmd.WallpaperIndex.Value;
                                        string title = manifest.Wallpapers[Math.Clamp(idx, 1, manifest.Wallpapers.Count) - 1].Title;
                                        return new IpcResponse(true, $"Swapping to wallpaper #{idx} ('{title}').");
                                    }
                                    else
                                    {
                                        return new IpcResponse(false, $"Failed to hot-swap to wallpaper #{cmd.WallpaperIndex.Value}. Check log for details.");
                                    }
                                }
                                else
                                {
                                    await _orchestrator.ResumeAsync();
                                    return new IpcResponse(true, "Playback resumed.");
                                }

                            case "Pause":
                                await _orchestrator.PauseAsync();
                                return new IpcResponse(true, "Playback paused.");

                            case "Resume":
                                await _orchestrator.ResumeAsync();
                                return new IpcResponse(true, "Playback resumed.");

                            case "Stop":
                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(100);
                                    cts.Cancel();
                                });
                                return new IpcResponse(true, "Service shutting down gracefully.");

                            case "Set":
                                if (string.IsNullOrEmpty(cmd.VideoPath))
                                {
                                    return new IpcResponse(false, "Error: Video path not specified.");
                                }
                                bool setSuccess = await _orchestrator.SetCustomVideoAsync(cmd.VideoPath);
                                if (setSuccess)
                                {
                                    return new IpcResponse(true, $"Swapped to custom video path: {cmd.VideoPath}");
                                }
                                else
                                {
                                    return new IpcResponse(false, "Failed to swap to custom video. Check log for details.");
                                }

                            default:
                                return new IpcResponse(false, $"Error: Unknown action '{cmd.Action}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        return new IpcResponse(false, $"Error processing command: {ex.Message}");
                    }
                }, cts.Token);
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

                    await ProcessCommandAsync(line, cts.Token);
                }
            }

            Console.WriteLine("\nShutting down engine and releasing all resources...");
            if (_orchestrator != null)
            {
                await _orchestrator.ShutdownAsync();
                _orchestrator.Dispose();
                _orchestrator = null;
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation cancelled.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal engine error: {ex}");
            return 1;
        }
        finally
        {
            if (_orchestrator != null)
            {
                try
                {
                    await _orchestrator.ShutdownAsync();
                    _orchestrator.Dispose();
                }
                catch { }
            }
        }
    }

    private static async Task ProcessCommandAsync(string commandLine, CancellationToken cancellationToken)
    {
        if (_orchestrator == null) return;

        var parts = commandLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string command = parts[0].ToLowerInvariant();
        string args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (command)
        {
            case "swap":
                if (int.TryParse(args, out int newIndex))
                {
                    await _orchestrator.SwapWallpaperAsync(newIndex);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Please specify a valid wallpaper index. Example: swap 2");
                    Console.ResetColor();
                }
                break;

            case "pause":
                await _orchestrator.PauseAsync();
                break;

            case "play":
                await _orchestrator.ResumeAsync();
                break;

            case "mem":
                // Trigger memory trim inside the orchestrator
                if (_orchestrator != null)
                {
                    await _orchestrator.SwapWallpaperAsync(_orchestrator.CurrentWallpaperIndex); // Quick swap to same forces a clean restart and trim
                }
                break;

            case "layout":
                if (Enum.TryParse<WallpaperLayoutMode>(args, true, out var mode))
                {
                    await _orchestrator.ApplyLayoutModeAsync(mode);
                    Console.WriteLine($"Layout mode updated to: {mode}");
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

    private static void PrintBanner()
    {
        Console.Title = "Wallpaper Turbo";
    }

    private static void PrintTopology(IEnumerable<GpuInfo> gpus)
    {
        Console.WriteLine("=== Wallpaper Turbo — Detected GPU Topology ===\n");

        List<GpuInfo> list = new(gpus);
        if (list.Count == 0)
        {
            Console.WriteLine("No GPUs detected.");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            GpuInfo gpu = list[i];
            Console.WriteLine($"GPU #{i + 1}: {gpu.Name}");
            Console.WriteLine($"  Vendor     : {gpu.Vendor}");
            Console.WriteLine($"  Dedicated  : {gpu.IsDedicated}");
            Console.WriteLine($"  VRAM       : {FormatBytes(gpu.VramBytes)}");
            Console.WriteLine();
        }

        MonitorInfo monitor = MonitorManager.GetPrimaryMonitor();
        Console.WriteLine($"{monitor.DeviceName} | {monitor.Width}x{monitor.Height} | Primary: {monitor.IsPrimary}");
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes == 0)
            return "Unknown";

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format("{0:0.##} {1}", value, units[unit]);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format("{0:0.##} {1}", value, units[unit]);
    }

    private const int ProcessPowerThrottling = 13;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x2;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation,
        uint processInformationSize);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private static void DisablePowerThrottling()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                StateMask = 0
            };

            using var process = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, (uint)System.Runtime.InteropServices.Marshal.SizeOf(state));
            
            process.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
            Console.WriteLine("[System] Opted-out of EcoQoS power throttling and set priority class to AboveNormal.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[System] Warning: Failed to disable power throttling: {ex.Message}");
        }
    }

    private static void StopRunningInstances()
    {
        uint currentId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        var targetPids = new System.Collections.Generic.HashSet<uint>();

        try
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
                if (NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0)
                {
                    string className = sb.ToString();
                    if (className.Equals("WallpaperTurbo_RenderWindow_Class", StringComparison.OrdinalIgnoreCase))
                    {
                        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                        if (pid != 0 && pid != currentId)
                        {
                            targetPids.Add(pid);
                        }
                    }
                }

                NativeMethods.EnumChildWindows(hwnd, (childHwnd, __) =>
                {
                    System.Text.StringBuilder sbChild = new System.Text.StringBuilder(256);
                    if (NativeMethods.GetClassName(childHwnd, sbChild, sbChild.Capacity) > 0)
                    {
                        string className = sbChild.ToString();
                        if (className.Equals("WallpaperTurbo_RenderWindow_Class", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeMethods.GetWindowThreadProcessId(childHwnd, out uint pid);
                            if (pid != 0 && pid != currentId)
                            {
                                targetPids.Add(pid);
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stop] Warning: Error enumerating windows: {ex.Message}");
        }

        string currentName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        var candidateNames = new System.Collections.Generic.List<string> { "dotnet", "WallpaperTurbo.AppRunner", currentName };

        try
        {
            foreach (string processName in candidateNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    if (process.Id == (int)currentId)
                        continue;

                    bool isWallpaperProcess = false;

                    if (processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            foreach (System.Diagnostics.ProcessModule module in process.Modules)
                            {
                                if (module.ModuleName.Contains("WallpaperTurbo", StringComparison.OrdinalIgnoreCase) ||
                                    module.ModuleName.Contains("LibVLCSharp", StringComparison.OrdinalIgnoreCase))
                                {
                                    isWallpaperProcess = true;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Protect against access-denied or architecture mismatch
                        }
                    }
                    else
                    {
                        isWallpaperProcess = true;
                    }

                    if (isWallpaperProcess)
                    {
                        targetPids.Add((uint)process.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stop] Warning: Error checking process names: {ex.Message}");
        }

        try
        {
            int stoppedCount = 0;
            foreach (uint pid in targetPids)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                    process.Kill(true); // Kill process and its children recursively
                    process.WaitForExit(3000);
                    stoppedCount++;
                    Console.WriteLine($"Successfully terminated Wallpaper Turbo process (PID: {pid}).");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to stop process {pid}: {ex.Message}");
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
            Console.WriteLine($"Error terminating processes: {ex.Message}");
        }
    }
}
