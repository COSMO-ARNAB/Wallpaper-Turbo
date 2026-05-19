// DesktopWindowInspector.cs - Provides functionality to inspect and dump the hierarchy of desktop windows in Wallpaper Turbo.
using System;
using System.Runtime.InteropServices;
using System.Text;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public static class DesktopWindowInspector
{
    public static void DumpShellWindows()
    {
        Console.WriteLine();
        //Console.WriteLine("=== Desktop Window Topology ===");

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var className = new StringBuilder(256);

            NativeMethods.GetClassName(
                hwnd,
                className,
                className.Capacity);

            Console.WriteLine(
                $"HWND=0x{hwnd.ToInt64():X} | CLASS={className}");

            DumpChildren(hwnd);

            return true;

        }, IntPtr.Zero);

        Console.WriteLine("===============================");
        Console.WriteLine();
    }

    private static void DumpChildren(
        IntPtr parent)
    {
        IntPtr child = IntPtr.Zero;

        while (true)
        {
            child = FindWindowEx(
                parent,
                child,
                null,
                null);

            if (child == IntPtr.Zero)
                break;

            var className =
                new StringBuilder(256);

            NativeMethods.GetClassName(
                child,
                className,
                className.Capacity);

            Console.WriteLine(
                $"   CHILD=0x{child.ToInt64():X} | CLASS={className}");
        }
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr hwndParent,
        IntPtr hwndChildAfter,
        string? lpszClass,
        string? lpszWindow);
}