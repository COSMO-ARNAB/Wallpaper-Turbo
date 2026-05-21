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

namespace WallpaperTurbo.AppRunner;

internal static class Program
{
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

        IntPtr hwnd =
            IntPtr.Zero;

        IMediaPipeline? pipeline =
            null;

        WallpaperSessionManager? sessionManager =
            null;

        try
        {
            PrintBanner();

            IHardwareDetector detector =
                new WindowsHardwareDetector();

            IEnumerable<GpuInfo> gpus =
                await detector
                    .GetGpusAsync(cts.Token)
                    .ConfigureAwait(false);

            MonitorInfo monitor =
                MonitorManager.GetPrimaryMonitor();

            PrintTopology(gpus);

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

            Console.WriteLine(
                $"\nRender HWND: {PtrToString(hwnd)}");

            DesktopWindowInspector
                .DumpShellWindows();

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
            WallpaperEntry wallpaper =
                SelectWallpaper();

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

            Console.ForegroundColor =
                ConsoleColor.Green;

            Console.WriteLine(
                "\nWallpaper Turbo is running! Press [ENTER] to exit cleanly.");

            Console.ResetColor();

            Console.ReadLine();

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

    private static WallpaperEntry SelectWallpaper()
    {
        string manifestPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "WallpaperManifest.json");

        WallpaperManifest manifest =
            WallpaperLibrary.Load(
                manifestPath);

        Console.WriteLine(
            "Available Wallpapers:\n");

        for (int i = 0;
             i < manifest.Wallpapers.Count;
             i++)
        {
            WallpaperEntry item =
                manifest.Wallpapers[i];

            Console.WriteLine(
                $"[{i + 1}] {item.Title}  —  {item.Author}");
        }

        Console.Write(
            "\nSelect wallpaper number: ");

        string? input =
            Console.ReadLine();

        if (!int.TryParse(
                input,
                out int selection))
        {
            selection = 1;
        }

        selection =
            Math.Clamp(
                selection - 1,
                0,
                manifest.Wallpapers.Count - 1);

        return manifest.Wallpapers[selection];
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
}