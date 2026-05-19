using System;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class DesktopCompositionStrategy
    : IDesktopHostStrategy
{
    private const uint WM_SPAWN_WORKER = 0x052C;

    private readonly IntPtr _progman;
    private readonly IntPtr _shellView;
    private readonly IntPtr _workerW;

    public string Name =>
        "Desktop Composition Strategy";

    public DesktopCompositionStrategy()
    {
        _progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

        NativeMethods.SendMessageTimeout(
            _progman,
            WM_SPAWN_WORKER,
            UIntPtr.Zero,
            IntPtr.Zero,
            0,
            1000,
            out _);

        IntPtr shellView = IntPtr.Zero;
        IntPtr workerW = IntPtr.Zero;

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
                shellView = currentShellView;

                workerW =
                    NativeMethods.FindWindowEx(
                        IntPtr.Zero,
                        hwnd,
                        "WorkerW",
                        null);

                return false;
            }

            return true;

        }, IntPtr.Zero);

        _shellView = shellView;
        _workerW = workerW;
    }

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

        if (_progman == IntPtr.Zero)
            return false;

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

        IntPtr parent =
            NativeMethods.SetParent(
                hwnd,
                _progman);

        if (parent == IntPtr.Zero &&
            NativeMethods.GetParent(hwnd) != _progman)
        {
            return false;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            _shellView,
            monitor.X,
            monitor.Y,
            monitor.Width,
            monitor.Height,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));

        bool visible =
            NativeMethods.IsWindowVisible(hwnd);

        // IntPtr currentParent =
        //     NativeMethods.GetParent(hwnd);

        // Console.WriteLine(
        //     $"[{Name}] Parent: 0x{currentParent.ToInt64():X}");

        // return visible &&
        //        currentParent == _progman;

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

        Console.WriteLine(
            $"[{Name}] Visible={visible} Child={isChild} Layered={isLayered}");

        return visible &&
               isChild &&
               isLayered;
    }
}