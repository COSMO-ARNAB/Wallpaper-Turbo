//Desktop composition strategy:
using System;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class DesktopCompositionStrategy
    : IDesktopHostStrategy
{
    public string Name =>
        "Desktop Composition Strategy";

    public bool IsSupported()
    {
        return DesktopUtil.GetProgman() != IntPtr.Zero;
    }

    public bool TryAttach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        ArgumentNullException.ThrowIfNull(monitor);

        //
        // 1. Sanitize window styles BEFORE attach.
        //
        WindowUtil.BorderlessWinStyle(hwnd);

        //
        // 2. Resolve desktop topology.
        //
        IntPtr progman =
            DesktopUtil.GetProgman();

        IntPtr workerW =
            DesktopUtil.GetDesktopWorkerW();

        IntPtr shellView =
            DesktopUtil.GetDesktopShellView();

        if (progman == IntPtr.Zero ||
            workerW == IntPtr.Zero)
        {
            return false;
        }

        // Lock shellView handle for WndProc Z-order enforcement
        NativeRenderWindow.ShellViewHandle = shellView;

        //
        // 3. First absolute fullscreen positioning (before parenting).
        //
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

        //
        // 4. Attach & Style according to Windows 11 Raised Desktop vs Standard Desktop.
        //
        bool attachSuccess = false;
        IntPtr expectedParent = IntPtr.Zero;

        if (DesktopUtil.IsRaisedDesktop())
        {
            expectedParent = progman;

            // Make child window and set WS_EX_LAYERED transparency to 255.
            WindowUtil.SetWindowStyle(hwnd, (long)NativeMethods.WindowStyles.WS_CHILD);
            WindowUtil.SetWindowExStyle(hwnd, (long)(NativeMethods.WindowStyles.WS_EX_NOACTIVATE | NativeMethods.WindowStyles.WS_EX_TRANSPARENT));
            WindowUtil.SetWindowTransparency(hwnd, 255);

            NativeMethods.RECT prct = new NativeMethods.RECT();
            NativeMethods.MapWindowPoints(hwnd, progman, ref prct, 2);

            if (WindowUtil.TrySetParent(hwnd, progman))
            {
                attachSuccess = true;

                // Position it directly under the shell view (desktop icons) and set relative bounds in one atomic call.
                if (shellView != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(
                        hwnd,
                        shellView,
                        prct.Left,
                        prct.Top,
                        monitor.Width,
                        monitor.Height,
                        (uint)NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE);
                }
                else
                {
                    NativeMethods.SetWindowPos(
                        hwnd,
                        IntPtr.Zero,
                        prct.Left,
                        prct.Top,
                        monitor.Width,
                        monitor.Height,
                        (uint)(
                            NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                            NativeMethods.SetWindowPosFlags.SWP_NOZORDER));
                }

                // Keep WorkerW at bottom.
                EnsureWorkerWZOrder(progman, workerW);
            }
        }
        else
        {
            expectedParent = workerW;

            NativeMethods.RECT prct = new NativeMethods.RECT();
            NativeMethods.MapWindowPoints(hwnd, workerW, ref prct, 2);

            if (WindowUtil.TrySetParent(hwnd, workerW))
            {
                attachSuccess = true;

                // Relative sizing after parenting.
                NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    prct.Left,
                    prct.Top,
                    monitor.Width,
                    monitor.Height,
                    (uint)(
                        NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                        NativeMethods.SetWindowPosFlags.SWP_NOZORDER));
            }
        }

        if (!attachSuccess)
        {
            return false;
        }

        IntPtr parent =
            NativeMethods.GetParent(hwnd);

        Console.WriteLine(
            $"[{Name}] Parent=0x{parent.ToInt64():X}");

        Console.WriteLine(
            $"[{Name}] Progman=0x{progman.ToInt64():X}");

        Console.WriteLine(
            $"[{Name}] WorkerW=0x{workerW.ToInt64():X}");

        Console.WriteLine(
            $"[{Name}] ShellView=0x{shellView.ToInt64():X}");

        Console.WriteLine(
            $"[{Name}] RaisedDesktop={DesktopUtil.IsRaisedDesktop()}");

        return ValidateAttachment(
            hwnd,
            expectedParent);
    }

    public void Detach(
        IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetParent(
            hwnd,
            IntPtr.Zero);
    }

    private static void EnsureWorkerWZOrder(
        IntPtr progman,
        IntPtr workerW)
    {
        if (workerW == IntPtr.Zero)
            return;

        IntPtr lastChild =
            WindowUtil.GetLastChildWindow(
                progman);

        if (lastChild == workerW)
            return;

        NativeMethods.SetWindowPos(
            workerW,
            NativeMethods.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOMOVE |
                NativeMethods.SetWindowPosFlags.SWP_NOSIZE |
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                NativeMethods.SetWindowPosFlags.SWP_NOOWNERZORDER |
                NativeMethods.SetWindowPosFlags.SWP_NOSENDCHANGING));
    }

    private static bool ValidateAttachment(
        IntPtr hwnd,
        IntPtr expectedParent)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return false;

        if (!NativeMethods.IsWindowVisible(hwnd))
            return false;

        IntPtr parent =
            NativeMethods.GetParent(hwnd);

        Console.WriteLine(
            $"[Validate] ActualParent=0x{parent.ToInt64():X}");

        if (parent == IntPtr.Zero)
            return false;

        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        bool isChild =
            ((uint)style &
             (uint)NativeMethods.WindowStyles.WS_CHILD) != 0;

        bool noActivate =
            ((uint)exStyle &
             (uint)NativeMethods.WindowStyles.WS_EX_NOACTIVATE) != 0;

        return isChild &&
               noActivate &&
               (parent == expectedParent);
    }
}