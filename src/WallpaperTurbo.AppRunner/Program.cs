// Program.cs - Main entry point for Wallpaper Turbo.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Hardware.Models;
using WallpaperTurbo.Core.Media.Pipelines;
using WallpaperTurbo.Core.Rendering;
using WallpaperTurbo.Core.Wallpaper;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Models;
using WallpaperTurbo.Core.Rendering.Host;

namespace WallpaperTurbo.AppRunner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            PrintBanner();

            IHardwareDetector detector =
                new WindowsHardwareDetector();

            var gpus = await detector
                .GetGpusAsync(cts.Token)
                .ConfigureAwait(false);

            var monitor =
                MonitorManager.GetPrimaryMonitor();

            PrintTopology(gpus);

            var wallpaperManager =
                new WindowsWallpaperManager(Console.WriteLine);

            var hwnd =
                await NativeRenderWindow.CreateAsync(monitor);

            Console.WriteLine(
                $"\nVideo Canvas created: {PtrToString(hwnd)}");

            DesktopWindowInspector.DumpShellWindows();


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

            Console.WriteLine(
                "Initializing Hardware Decode Pipeline...");

            var pipeline =
                new HardwareDecodePipeline();

            pipeline.Initialize(hwnd);

            var wallpaper =
                SelectWallpaper();

            var videoPath = Path.Combine(
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
                ShowMissingWallpaperWarning(videoPath);
                return 1;
            }

            Console.WriteLine(
                $"Loading media stream: {videoPath}");

            pipeline.LoadMedia(videoPath);

            using var sessionManager =
                new WallpaperSessionManager();

            var session = new WallpaperSession(
                hwnd,
                wallpaper,
                pipeline,
                monitor);

            sessionManager.AddSession(session);

            session.Play();

            Console.ForegroundColor =
                ConsoleColor.Green;

            Console.WriteLine(
                "\nWallpaper Turbo is running! Press [ENTER] to exit cleanly.");

            Console.ResetColor();

            Console.ReadLine();

            Console.WriteLine(
                "Releasing GPU resources...");

            NativeRenderWindow.Shutdown(hwnd);

            return 0;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation cancelled.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Fatal engine error: {ex.Message}");

                return 1;
            }

    }

    private static WallpaperEntry SelectWallpaper()
    {
        string manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "WallpaperManifest.json");

        var manifest =
            WallpaperLibrary.Load(manifestPath);

        Console.WriteLine(
            "Available Wallpapers:\n");

        for (int i = 0; i < manifest.Wallpapers.Count; i++)
        {
            var item = manifest.Wallpapers[i];

            Console.WriteLine(
                $"[{i + 1}] {item.Title}  —  {item.Author}");
        }

        Console.Write(
            "\nSelect wallpaper number: ");

        string? input = Console.ReadLine();

        if (!int.TryParse(input, out int selection))
        {
            selection = 1;
        }

        selection = Math.Clamp(
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
        Console.Title = "Wallpaper Turbo";
    }

    private static void PrintTopology(
        IEnumerable<GpuInfo> gpus)
    {
        Console.WriteLine(
            "=== Wallpaper Turbo — Detected GPU Topology ===\n");

        var list = new List<GpuInfo>(gpus);

        if (list.Count == 0)
        {
            Console.WriteLine("No GPUs detected.");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var gpu = list[i];

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
        var monitors = MonitorManager.GetPrimaryMonitor();

        Console.WriteLine(
            $"{monitors.DeviceName} | {monitors.Width}x{monitors.Height} | Primary: {monitors.IsPrimary}");
    }

    private static string FormatBytes(
        ulong bytes)
    {
        if (bytes == 0)
            return "Unknown";

        string[] units =
        {
            "B", "KB", "MB", "GB", "TB"
        };

        double value = bytes;

        int unit = 0;

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