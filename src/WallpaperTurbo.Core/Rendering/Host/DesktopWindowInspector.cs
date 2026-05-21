// DesktopWindowInspector.cs

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
        Console.WriteLine("=== Desktop Window Topology ===");

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            DumpWindow(hwnd, 0);

            DumpChildren(
                hwnd,
                1);

            return true;

        }, IntPtr.Zero);

        Console.WriteLine("================================");
        Console.WriteLine();
    }

    private static void DumpChildren(
        IntPtr parent,
        int depth)
    {
        IntPtr child =
            IntPtr.Zero;

        while (true)
        {
            child =
                NativeMethods.FindWindowEx(
                    parent,
                    child,
                    null,
                    null);

            if (child == IntPtr.Zero)
                break;

            DumpWindow(
                child,
                depth);

            DumpChildren(
                child,
                depth + 1);
        }
    }

    private static void DumpWindow(
        IntPtr hwnd,
        int depth)
    {
        var className =
            new StringBuilder(256);

        NativeMethods.GetClassName(
            hwnd,
            className,
            className.Capacity);

        IntPtr parent =
            NativeMethods.GetParent(hwnd);

        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        bool visible =
            NativeMethods.IsWindowVisible(hwnd);

        string indent =
            new string(' ', depth * 3);

        Console.WriteLine(
            $"{indent}HWND=0x{hwnd.ToInt64():X} " +
            $"CLASS={className} " +
            $"PARENT=0x{parent.ToInt64():X} " +
            $"STYLE=0x{style:X8} " +
            $"EXSTYLE=0x{exStyle:X8} " +
            $"VISIBLE={visible}");
    }
}