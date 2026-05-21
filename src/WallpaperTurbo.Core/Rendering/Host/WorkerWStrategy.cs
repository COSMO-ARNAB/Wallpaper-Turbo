// WorkerWStrategy.cs

using System;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class WorkerWStrategy
    : IDesktopHostStrategy
{
    private const uint WM_SPAWN_WORKER =
        0x052C;

    public string Name =>
        "Legacy WorkerW Strategy";

    public bool IsSupported()
    {
        //
        // Only fallback for non-raised desktops.
        //
        return !WindowsCapabilityDetector
            .HasRaisedDesktopComposition();
    }

    public bool TryAttach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        if (!TryResolveWorkerW(
                out IntPtr workerW))
        {
            return false;
        }

        ApplyLegacyStyles(hwnd);

        if (NativeMethods.SetParent(
                hwnd,
                workerW) == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
        NativeMethods.MapWindowPoints(IntPtr.Zero, workerW, ref prct, 2);

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            prct.Left,
            prct.Top,
            monitor.Width,
            monitor.Height,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));

        IntPtr parent =
            NativeMethods.GetParent(hwnd);

        Console.WriteLine(
            $"[{Name}] Parent=0x{parent.ToInt64():X}");

        return parent == workerW;
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

    private static bool TryResolveWorkerW(
        out IntPtr workerW)
    {
        workerW =
            IntPtr.Zero;

        IntPtr progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

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

        IntPtr resolvedWorker =
            IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            IntPtr shellView =
                NativeMethods.FindWindowEx(
                    hwnd,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);

            if (shellView != IntPtr.Zero)
            {
                resolvedWorker =
                    NativeMethods.FindWindowEx(
                        IntPtr.Zero,
                        hwnd,
                        "WorkerW",
                        null);

                return false;
            }

            return true;

        }, IntPtr.Zero);

        workerW =
            resolvedWorker;

        return workerW != IntPtr.Zero;
    }

    private static void ApplyLegacyStyles(
        IntPtr hwnd)
    {
        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        style &=
            unchecked((int)~(uint)
                NativeMethods.WindowStyles.WS_POPUP);

        style |=
            (int)(
                NativeMethods.WindowStyles.WS_CHILD |
                NativeMethods.WindowStyles.WS_VISIBLE |
                NativeMethods.WindowStyles.WS_CLIPSIBLINGS |
                NativeMethods.WindowStyles.WS_CLIPCHILDREN);

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_STYLE,
            style);

        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        //
        // Modernized fallback:
        // NO layered window.
        //
        exStyle |=
            (int)(
                NativeMethods.WindowStyles.WS_EX_TOOLWINDOW |
                NativeMethods.WindowStyles.WS_EX_NOACTIVATE);

        exStyle &=
            unchecked((int)~(uint)
                NativeMethods.WindowStyles.WS_EX_APPWINDOW);

        exStyle &=
            unchecked((int)~(uint)
                NativeMethods.WindowStyles.WS_EX_LAYERED);

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            exStyle);
    }
}