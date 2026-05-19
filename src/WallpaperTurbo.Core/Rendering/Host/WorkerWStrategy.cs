// WorkerWStrategy.cs - Implements the WorkerW strategy for attaching windows to the desktop in Wallpaper Turbo.
using System;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class WorkerWStrategy
    : IDesktopHostStrategy
{
    private const uint WM_SPAWN_WORKER = 0x052C;

    private readonly IntPtr _workerW;

    public string Name =>
        "Legacy WorkerW Strategy";

    public WorkerWStrategy()
    {
        IntPtr progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

        NativeMethods.SendMessageTimeout(
            progman,
            WM_SPAWN_WORKER,
            UIntPtr.Zero,
            IntPtr.Zero,
            0,
            1000,
            out _);

        IntPtr worker = IntPtr.Zero;

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
                worker =
                    NativeMethods.FindWindowEx(
                        IntPtr.Zero,
                        hwnd,
                        "WorkerW",
                        null);

                return false;
            }

            return true;

        }, IntPtr.Zero);

        _workerW = worker;
    }

    public bool IsSupported()
    {
        return _workerW != IntPtr.Zero;
    }

    public bool TryAttach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        if (_workerW == IntPtr.Zero)
            return false;

        NativeMethods.SetParent(
            hwnd,
            _workerW);

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

        IntPtr parent =
            NativeMethods.GetParent(hwnd);

        Console.WriteLine(
            $"[{Name}] Parent: 0x{parent.ToInt64():X}");

        return parent == _workerW;
    }
}