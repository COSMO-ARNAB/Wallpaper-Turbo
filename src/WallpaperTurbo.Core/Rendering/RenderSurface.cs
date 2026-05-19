using System;
using System.Runtime.InteropServices;

namespace WallpaperTurbo.Core.Rendering;

public static class RenderSurface
{
    private const int WS_POPUP =
        unchecked((int)0x80000000);

    private const int WS_VISIBLE =
        0x10000000;

    private const int SW_SHOW = 5;

    public static IntPtr Create(
        string className,
        int x,
        int y,
        int width,
        int height)
    {
        var hInstance =
            GetModuleHandle(null);

        var hwnd = CreateWindowExW(
            0,
            className,
            "Wallpaper Turbo Video Canvas",
            WS_POPUP | WS_VISIBLE,
            x,
            y,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        ShowWindow(hwnd, SW_SHOW);

        UpdateWindow(hwnd);

        return hwnd;
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr GetModuleHandle(
        string? lpModuleName);

    [DllImport(
        "user32.dll",
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(
        IntPtr hWnd);
}