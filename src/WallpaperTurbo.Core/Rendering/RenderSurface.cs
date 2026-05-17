// RenderSurface.cs - Contains logic to create a native window that serves as the rendering surface for our video wallpaper, which
using System;
using System.Runtime.InteropServices;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering;

public static class RenderSurface
{
    private const int WS_POPUP =
        unchecked((int)0x80000000);

    private const int WS_VISIBLE =
        0x10000000;

    private const int SW_SHOW = 5;

    public static IntPtr Create(
        string className)
    {
        var hInstance =
            GetModuleHandle(null);

        var hwnd = CreateWindowExW(
            0x08000000 | 0x00000080,
            className,
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
            return IntPtr.Zero;

        ShowWindow(hwnd, SW_SHOW);

        UpdateWindow(hwnd);

        NativeMethods.SetWindowPos(
            hwnd,
            new IntPtr(1),
            0,
            0,
            0,
            0,
            0x0002 | 0x0001 | 0x0010);

        return hwnd;
    }

    [DllImport("kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr GetModuleHandle(
        string? lpModuleName);

    [DllImport("user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll",
        SetLastError = true)]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow);

    [DllImport("user32.dll",
        SetLastError = true)]
    private static extern bool UpdateWindow(
        IntPtr hWnd);
}