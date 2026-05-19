using System;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class DesktopCompositionStrategy
    : IDesktopHostStrategy
{
    private const uint WM_SPAWN_WORKER = 0x052C;

    public string Name =>
        "Desktop Composition Strategy";

    public bool IsSupported()
    {
        return WindowsCapabilityDetector
            .HasRaisedDesktopComposition();
    }

    public bool TryAttach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        if (!TryResolveDesktopTopology(
                out IntPtr progman,
                out IntPtr shellView))
        {
            return false;
        }

        ApplyCompositionStyles(hwnd);

        NativeMethods.SetParent(
            hwnd,
            progman);

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            monitor.X,
            monitor.Y,
            monitor.Width,
            monitor.Height,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));

        return ValidateAttachment(hwnd);
    }

    private static bool TryResolveDesktopTopology(
        out IntPtr progman,
        out IntPtr shellView)
    {
        progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

        shellView = IntPtr.Zero;

        if (progman == IntPtr.Zero)
            return false;
        
        NativeMethods.SendMessageTimeout(
        progman,
        WM_SPAWN_WORKER,
        UIntPtr.Zero,
        IntPtr.Zero,
        0,
        1000,
        out _);

        IntPtr resolvedShellView = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            IntPtr currentShellView =
                NativeMethods.FindWindowEx(
                    hwnd,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);
        
            if (currentShellView != IntPtr.Zero)
            {
                resolvedShellView = currentShellView;
                return false;
            }
        
            return true;
        
        }, IntPtr.Zero);
        
        shellView = resolvedShellView;

        //Console.WriteLine(
        //$"Progman=0x{progman.ToInt64():X} ShellView=0x{shellView.ToInt64():X}");
        
        return shellView != IntPtr.Zero;
    }

    private static void ApplyCompositionStyles(
        IntPtr hwnd)
    {
        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        style |=
            (int)NativeMethods.WindowStyles.WS_CHILD;

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_STYLE,
            style);

        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        exStyle |=
            (int)NativeMethods.WindowStyles.WS_EX_LAYERED;

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            exStyle);

        NativeMethods.SetLayeredWindowAttributes(
            hwnd,
            0,
            255,
            NativeMethods.LWA_ALPHA);
    }

    private static bool ValidateAttachment(
        IntPtr hwnd)
    {
        bool visible =
            NativeMethods.IsWindowVisible(hwnd);

        int currentStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        int currentExStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        bool isChild =
            ((uint)currentStyle &
             (uint)NativeMethods.WindowStyles.WS_CHILD) != 0;

        bool isLayered =
            ((uint)currentExStyle &
             (uint)NativeMethods.WindowStyles.WS_EX_LAYERED) != 0;

        return visible &&
               isChild &&
               isLayered;
    }
}