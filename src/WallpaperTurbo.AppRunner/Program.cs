//Program.cs - Main entry point for the Wallpaper Turbo application runner.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Hardware.Models;
using WallpaperTurbo.Core.Interop;
using WallpaperTurbo.Core.Wallpaper;
using WallpaperTurbo.Core.Media.Pipelines;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Rendering;

namespace WallpaperTurbo.AppRunner
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

            IHardwareDetector detector = new WindowsHardwareDetector();

            try
            {
                // 1. Hardware Detection
                var gpus = await detector.GetGpusAsync(cts.Token).ConfigureAwait(false);
                PrintTopology(gpus);

                // 2. Initialize wallpaper manager and prepare the WorkerW target.
                var wallpaperManager = new WindowsWallpaperManager(msg => Console.WriteLine(msg));
                wallpaperManager.InitializeDesktopHandle();

                // 3. Create a native canvas window to hold our video.
                var hwnd = await NativeRenderWindow.CreateAsync();
                //wallpaperManager.AttachWindow(hwnd);
                //Console.WriteLine("Attached render surface to WorkerW.");
                Console.WriteLine($"\nVideo Canvas created: {PtrToString(hwnd)}");

                /*try
                {
                    // Push the canvas behind the desktop icons
                    //wallpaperManager.AttachWindow(hwnd);
                    //Console.WriteLine("Attached video canvas to desktop WorkerW.");
                    //await Task.Delay(500);

                    NativeMethods.SetWindowPos(
                        hwnd,
                        new IntPtr(1), // HWND_BOTTOM
                        0,
                        0,
                        0,
                        0,
                        0x0001 | 0x0002 | 0x0010 // NOSIZE | NOMOVE | NOACTIVATE
                    );
                    Console.WriteLine("Set video canvas to bottom of Z-order.");    
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to attach test window: {ex.Message}");
                    return 1;
                }*/

                // 4. Initialize Hardware-Accelerated Video Pipeline
                Console.WriteLine("Initializing Hardware Decode Pipeline...");
                var pipeline = new HardwareDecodePipeline();
                
                // Bind the VLC player directly to our background canvas
                pipeline.Initialize(hwnd);

                // 5. Load the video
                string manifestPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "WallpaperManifest.json"
                );

                var manifest = WallpaperLibrary.Load(manifestPath);

                // Display available wallpapers and prompt user to select one.
                Console.WriteLine("Available Wallpapers:\n");

                for (int i = 0; i < manifest.Wallpapers.Count; i++)
                {
                    var item = manifest.Wallpapers[i];

                    Console.WriteLine(
                        $"[{i + 1}] {item.Title}  —  {item.Author}"
                    );
                }

                Console.Write("\nSelect wallpaper number: ");

                string? input = Console.ReadLine();

                if (!int.TryParse(input, out int selection))
                {
                    selection = 1;
                }

                selection = Math.Clamp(
                    selection - 1,
                    0,
                    manifest.Wallpapers.Count - 1
                );

                var wallpaper = manifest.Wallpapers[selection];

                string videoPath = Path.Combine(
                    AppContext.BaseDirectory,
                    wallpaper.Video
                );

                Console.WriteLine($"Loaded wallpaper: {wallpaper.Title}");
                Console.WriteLine($"Author: {wallpaper.Author}");
                Console.WriteLine($"Video Source: {videoPath}\n");

                if (File.Exists(videoPath))
                {
                    Console.WriteLine($"Loading media stream: {videoPath}");
                    pipeline.LoadMedia(videoPath);
                    pipeline.Play();
                    // 4. Initialize Hardware-Accelerated Video Pipeline
                     //Console.WriteLine("Initializing Hardware Decode Pipeline...");
                     //var pipeline = new HardwareDecodePipeline();
                     //await Task.Delay(1000);

                    //wallpaperManager.AttachWindow(hwnd);
                    //Console.WriteLine("Attached video canvas to desktop WorkerW.");
                
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n==========================================================");
                    Console.WriteLine("WARNING: Video file not found!");
                    Console.WriteLine("Please edit Program.cs and point 'videoPath' to a real");
                    Console.WriteLine("local .mp4 file to see the live wallpaper render!");
                    Console.WriteLine($"Current target: {videoPath}");
                    Console.WriteLine("==========================================================\n");
                    Console.ResetColor();
                }

                // 6. Keep application alive
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nWallpaper Turbo is running! Press [ENTER] to exit cleanly.");
                Console.ResetColor();
                
                Console.ReadLine();

                // 7. Clean Shutdown
                Console.WriteLine("Releasing GPU resources...");
                pipeline.Release();
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
                Console.Error.WriteLine($"Fatal engine error: {ex.Message}");
                return 1;
            }
        }

        private static void PrintTopology(IEnumerable<GpuInfo> gpus)
        {
            Console.WriteLine("=== Wallpaper Turbo — Detected GPU Topology ===\n");
            var list = new List<GpuInfo>(gpus);
            if (list.Count == 0)
            {
                Console.WriteLine("No GPUs detected.");
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var gpu = list[i];
                Console.WriteLine($"GPU #{i + 1}: {gpu.Name}");
                Console.WriteLine($"  Vendor     : {gpu.Vendor}");
                Console.WriteLine($"  Dedicated  : {gpu.IsDedicated}");
                Console.WriteLine($"  VRAM       : {FormatBytes(gpu.VramBytes)}");
                Console.WriteLine();
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0)
                return "Unknown";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return string.Format("{0:0.##} {1}", value, units[unit]);
        }

        private static string PtrToString(IntPtr p)
        {
            if (p == IntPtr.Zero) return "0x0";
            return IntPtr.Size == 8 ? $"0x{p.ToInt64():X}" : $"0x{p.ToInt32():X}";
        }

        /// <summary>
        /// Minimal native test window helper that creates a basic colored window on a dedicated thread.
        /// </summary>
        /*private static class NativeTestWindow
        {
            private const int CS_VREDRAW = 0x0001;
            private const int CS_HREDRAW = 0x0002;
            private const int WS_POPUP = unchecked((int)0x80000000);
            //private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
            private const int WS_VISIBLE = 0x10000000;
            private const int SW_SHOW = 5;
            private const uint WM_DESTROY = 0x0002;
            private const uint WM_CLOSE = 0x0010;

            private static readonly string ClassName = "WallpaperTurbo_TestWindow_Class";

            public static Task<IntPtr> CreateAsync()
            {
                var tcs = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);

                var thread = new Thread(() =>
                {
                    try
                    {
                        var hInstance = GetModuleHandle(null);

                        WNDCLASSEXW wnd = new WNDCLASSEXW();
                        wnd.cbSize = Marshal.SizeOf<WNDCLASSEXW>();
                        wnd.style = CS_HREDRAW | CS_VREDRAW;
                        wnd.lpfnWndProc = WndProc;
                        wnd.cbClsExtra = 0;
                        wnd.cbWndExtra = 0;
                        wnd.hInstance = hInstance;
                        wnd.hCursor = LoadCursor(IntPtr.Zero, (IntPtr)32512); // IDC_ARROW
                        wnd.hbrBackground = CreateSolidBrush(RGB(0, 0, 0)); // Black background
                        wnd.lpszClassName = ClassName;

                        var atom = RegisterClassExW(ref wnd);
                        if (atom == 0)
                        {
                            tcs.SetException(new InvalidOperationException("RegisterClassEx failed."));
                            return;
                        }

                        // Start at 0,0 and make it 1920x1080 (VLC will auto-scale, but good to have a base size)
                        var hwnd = CreateWindowExW(
                             0x08000000 | 0x00000080, // WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW (prevents showing in Alt-Tab)
                             ClassName,
                             "Wallpaper Turbo Video Canvas",
                             WS_POPUP | WS_VISIBLE,
                             0,
                             0,
                             NativeMethods.GetSystemMetrics(0),
                             NativeMethods.GetSystemMetrics(1),
                             IntPtr.Zero,
                             IntPtr.Zero,
                             hInstance,
                             IntPtr.Zero);

                        if (hwnd == IntPtr.Zero)
                        {
                            UnregisterClassW(ClassName, hInstance);
                            tcs.SetException(new InvalidOperationException("CreateWindowEx failed."));
                            return;
                        }

                        ShowWindow(hwnd, SW_SHOW);
                        UpdateWindow(hwnd);
                        NativeMethods.SetWindowPos(
                            hwnd,
                            new IntPtr(1), // HWND_BOTTOM
                            0,
                            0,
                            0,
                            0,
                            0x0002 | 0x0001 | 0x0010 // NOSIZE | NOMOVE | NOACTIVATE
                        );

                        tcs.SetResult(hwnd);

                        // Standard message loop for this thread.
                        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) != 0)
                        {
                            TranslateMessage(ref msg);
                            DispatchMessage(ref msg);
                        }

                        // Clean up class when done.
                        UnregisterClassW(ClassName, hInstance);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }) { IsBackground = true };

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                return tcs.Task;
            }

            public static void Shutdown(IntPtr hwnd)
            {
                if (hwnd == IntPtr.Zero) return;
                PostMessage(hwnd, WM_CLOSE, UIntPtr.Zero, IntPtr.Zero);
            }

            private static IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
            {
                switch (msg)
                {
                    case WM_CLOSE:
                        DestroyWindow(hWnd);
                        return IntPtr.Zero;
                    case WM_DESTROY:
                        PostQuitMessage(0);
                        return IntPtr.Zero;
                }

                return DefWindowProcW(hWnd, msg, wParam, lParam);
            }

            #region Native declarations

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct WNDCLASSEXW
            {
                public int cbSize;
                public int style;
                [MarshalAs(UnmanagedType.FunctionPtr)]
                public WndProcDelegate lpfnWndProc;
                public int cbClsExtra;
                public int cbWndExtra;
                public IntPtr hInstance;
                public IntPtr hIcon;
                public IntPtr hCursor;
                public IntPtr hbrBackground;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string lpszMenuName;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string lpszClassName;
                public IntPtr hIconSm;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MSG
            {
                public IntPtr hwnd;
                public uint message;
                public UIntPtr wParam;
                public IntPtr lParam;
                public uint time;
                public POINT pt;
                public uint lPrivate;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT
            {
                public int x;
                public int y;
            }

            private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern ushort RegisterClassExW([In] ref WNDCLASSEXW lpwcx);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateWindowExW(
                int dwExStyle,
                [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
                [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
                int dwStyle,
                int x,
                int y,
                int nWidth,
                int nHeight,
                IntPtr hWndParent,
                IntPtr hMenu,
                IntPtr hInstance,
                IntPtr lpParam);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UpdateWindow(IntPtr hWnd);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool UnregisterClassW([MarshalAs(UnmanagedType.LPWStr)] string lpClassName, IntPtr hInstance);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool DestroyWindow(IntPtr hWnd);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

            [DllImport("user32.dll")] 
            private static extern bool TranslateMessage([In] ref MSG lpMsg);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

            [DllImport("gdi32.dll", SetLastError = true)]
            private static extern IntPtr CreateSolidBrush(uint crColor);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern void PostQuitMessage(int nExitCode);

            private static uint RGB(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

            #endregion
        }*/
    }
}