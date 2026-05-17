using System;
using System.Runtime.InteropServices;
using System.Text;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering;

public static class DesktopAttachmentService
{
    private static IntPtr _workerW = IntPtr.Zero;

    private const uint WM_SPAWN_WORKER = 0x052C;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SENDMSG_TIMEOUT_MS = 1000;

    public static IntPtr WorkerWHandle => _workerW;

    public static void Initialize()
    {
        if (_workerW != IntPtr.Zero)
            return;

        var progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

        try
        {
            UIntPtr result;

            NativeMethods.SendMessageTimeout(
                progman,
                WM_SPAWN_WORKER,
                UIntPtr.Zero,
                IntPtr.Zero,
                SMTO_ABORTIFHUNG,
                SENDMSG_TIMEOUT_MS,
                out result);
        }
        catch
        {
        }

        IntPtr shellWindow = progman;

        IntPtr shellView =
            NativeMethods.FindWindowEx(
                progman,
                IntPtr.Zero,
                "SHELLDLL_DefView",
                null);

        if (shellView == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                IntPtr view =
                    NativeMethods.FindWindowEx(
                        hwnd,
                        IntPtr.Zero,
                        "SHELLDLL_DefView",
                        null);

                if (view != IntPtr.Zero)
                {
                    shellWindow = hwnd;
                    shellView = view;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }

        IntPtr worker =
            NativeMethods.FindWindowEx(
                IntPtr.Zero,
                shellWindow,
                "WorkerW",
                null);

        if (worker == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                var sb = new StringBuilder(256);

                var len =
                    NativeMethods.GetClassName(
                        hwnd,
                        sb,
                        sb.Capacity);

                if (string.Equals(
                    len > 0 ? sb.ToString() : string.Empty,
                    "WorkerW",
                    StringComparison.Ordinal))
                {
                    if (NativeMethods.FindWindowEx(
                        hwnd,
                        IntPtr.Zero,
                        "SHELLDLL_DefView",
                        null) == IntPtr.Zero)
                    {
                        worker = hwnd;
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);
        }

        _workerW = worker;
    }

    public static void Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        if (_workerW == IntPtr.Zero)
            Initialize();

        if (_workerW == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "WorkerW not found.");
        }

        NativeMethods.SetParent(
            hwnd,
            _workerW);

        int width =
            NativeMethods.GetSystemMetrics(
                NativeMethods.SM_CXSCREEN);

        int height =
            NativeMethods.GetSystemMetrics(
                NativeMethods.SM_CYSCREEN);

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_SHOWWINDOW);
    }
}