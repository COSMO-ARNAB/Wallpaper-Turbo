// DesktopShellStrategy.cs - Implements the Desktop Shell strategy for attaching windows to the desktop in Wallpaper Turbo.
using System;
using System.Runtime.InteropServices;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class DesktopShellStrategy : IDesktopHostStrategy
{
    public string Name => "DesktopShell";

    public bool IsSupported()
    {
        return NativeMethods.FindWindowW(
            "Progman",
            null) != IntPtr.Zero;
    }

    public bool TryAttach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (hwnd == IntPtr.Zero)
            return false;

        IntPtr progman = NativeMethods.FindWindowW(
            "Progman",
            null);

        if (progman == IntPtr.Zero)
            return false;

        NativeMethods.SetParent(hwnd, progman);
        Console.WriteLine(
        $"Parent HWND: 0x{GetParent(hwnd).ToInt64():X}");

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            monitor.X,
            monitor.Y,
            monitor.Width,
            monitor.Height,
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_SHOWWINDOW);

        return IsWindowVisible(hwnd) &&
               !IsWindowCloaked(hwnd);
    }

    public void Detach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetParent(hwnd, IntPtr.Zero);
    }

    private static bool IsWindowCloaked(
        IntPtr hwnd)
    {
        int cloaked = 0;

        int result = DwmGetWindowAttribute(
            hwnd,
            14,
            out cloaked,
            Marshal.SizeOf<int>());

        return result == 0 && cloaked != 0;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(
        IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(
    IntPtr hWnd);
}