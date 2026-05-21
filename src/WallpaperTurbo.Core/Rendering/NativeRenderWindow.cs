// NativeRenderWindow.cs

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Display;

namespace WallpaperTurbo.Core.Rendering;

public static class NativeRenderWindow
{
    private const uint WM_DESTROY =
        0x0002;

    private const uint WM_CLOSE =
        0x0010;

    private static readonly string ClassName =
        "WallpaperTurbo_RenderWindow_Class";

    public static Task<IntPtr> CreateAsync(
        MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(
            monitor);

        TaskCompletionSource<IntPtr> tcs =
            new(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        Thread renderThread =
            new(() =>
            {
                try
                {
                    //
                    // Register native render class.
                    //
                    WindowClassRegistrar.Register(
                        ClassName,
                        WndProc);

                    //
                    // Create neutral render surface.
                    //
                    IntPtr hwnd =
                        RenderSurface.Create(
                            ClassName,
                            monitor.X,
                            monitor.Y,
                            monitor.Width,
                            monitor.Height);

                    if (hwnd == IntPtr.Zero)
                    {
                        WindowClassRegistrar.Unregister(
                            ClassName);

                        tcs.SetException(
                            new InvalidOperationException(
                                "CreateWindowExW failed."));

                        return;
                    }

                    tcs.SetResult(hwnd);

                    //
                    // Native message/render loop.
                    //
                    RenderLoop.Run();

                    //
                    // Cleanup.
                    //
                    WindowClassRegistrar.Unregister(
                        ClassName);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

        renderThread.IsBackground =
            true;

        renderThread.SetApartmentState(
            ApartmentState.STA);

        renderThread.Start();

        return tcs.Task;
    }

    public static void Shutdown(
        IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        PostMessage(
            hwnd,
            WM_CLOSE,
            UIntPtr.Zero,
            IntPtr.Zero);
    }

    private static IntPtr WndProc(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam)
    {
        switch (msg)
        {
            case WM_CLOSE:
            {
                DestroyWindow(hWnd);
                return IntPtr.Zero;
            }

            case WM_DESTROY:
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
        }

        return DefWindowProcW(
            hWnd,
            msg,
            wParam,
            lParam);
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool DestroyWindow(
        IntPtr hWnd);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern IntPtr DefWindowProcW(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern void PostQuitMessage(
        int nExitCode);
}