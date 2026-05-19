// NativeRenderWindow.cs - Provides functionality to create and manage a native render window for wallpaper rendering in Wallpaper Turbo.
using System;
using System.Runtime.InteropServices;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering;

public static class NativeRenderWindow
{
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;

    private static readonly string ClassName =
        "WallpaperTurbo_TestWindow_Class";

    public static Task<IntPtr> CreateAsync()
    {
        var tcs = new TaskCompletionSource<IntPtr>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                WindowClassRegistrar.Register(
                    ClassName,
                    WndProc);

                var hwnd = RenderSurface.Create(
                    ClassName,
                    NativeMethods.GetSystemMetrics(0),
                    NativeMethods.GetSystemMetrics(1));

                if (hwnd == IntPtr.Zero)
                {
                    WindowClassRegistrar.Unregister(ClassName);

                    tcs.SetException(
                        new InvalidOperationException(
                            "CreateWindowEx failed."));

                    return;
                }

                tcs.SetResult(hwnd);

                RenderLoop.Run();

                WindowClassRegistrar.Unregister(ClassName);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    public static void Shutdown(IntPtr hwnd)
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
                DestroyWindow(hWnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProcW(
            hWnd,
            msg,
            wParam,
            lParam);
    }

    #region Native Declarations

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(
        IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProcW(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void PostQuitMessage(
        int nExitCode);

    #endregion
}