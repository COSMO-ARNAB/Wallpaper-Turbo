// DesktopUtil.cs

using System;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Interop;

public static class DesktopUtil
{
    private const uint WM_SPAWN_WORKER =
        0x052C;

    public static IntPtr GetProgman()
    {
        return NativeMethods.FindWindowW(
            "Progman",
            null);
    }

    public static bool IsRaisedDesktop()
    {
        IntPtr progman =
            GetProgman();

        if (progman == IntPtr.Zero)
            return false;

        return WindowUtil.HasExtendedStyle(
            progman,
            NativeMethods.WindowStyles.WS_EX_NOREDIRECTIONBITMAP);
    }

    public static void EnsureWorkerW()
    {
        IntPtr progman =
            GetProgman();

        if (progman == IntPtr.Zero)
            return;

        NativeMethods.SendMessageTimeout(
            progman,
            WM_SPAWN_WORKER,
            new UIntPtr(0x0D),
            new IntPtr(0x01),
            0,
            1000,
            out _);
    }

    public static IntPtr GetDesktopWorkerW()
    {
        EnsureWorkerW();

        IntPtr progman =
            GetProgman();

        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        if (IsRaisedDesktop())
        {
            IntPtr childWorker = NativeMethods.FindWindowEx(
                progman,
                IntPtr.Zero,
                "WorkerW",
                null);
            if (childWorker != IntPtr.Zero)
            {
                return childWorker;
            }
        }

        IntPtr workerW =
            IntPtr.Zero;

        IntPtr shellView =
            NativeMethods.FindWindowEx(
                progman,
                IntPtr.Zero,
                "SHELLDLL_DefView",
                null);

        //
        // If desktop icons are not under Progman,
        // enumerate WorkerW hierarchy.
        //
        if (shellView == IntPtr.Zero)
        {
            do
            {
                workerW =
                    NativeMethods.FindWindowEx(
                        NativeMethods.GetDesktopWindow(),
                        workerW,
                        "WorkerW",
                        null);

                shellView =
                    NativeMethods.FindWindowEx(
                        workerW,
                        IntPtr.Zero,
                        "SHELLDLL_DefView",
                        null);

            } while (
                shellView == IntPtr.Zero &&
                workerW != IntPtr.Zero);
        }

        //
        // Modern Win11 fallback.
        //
        return workerW != IntPtr.Zero
            ? workerW
            : progman;
    }

    public static IntPtr GetDesktopShellView()
    {
        IntPtr shellView =
            IntPtr.Zero;

        IntPtr workerW =
            IntPtr.Zero;

        IntPtr progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

        IntPtr desktop =
            NativeMethods.GetDesktopWindow();

        if (progman != IntPtr.Zero)
        {
            shellView =
                NativeMethods.FindWindowEx(
                    progman,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);

            //
            // Traverse WorkerWs dynamically.
            //
            if (shellView == IntPtr.Zero)
            {
                do
                {
                    workerW =
                        NativeMethods.FindWindowEx(
                            desktop,
                            workerW,
                            "WorkerW",
                            null);

                    shellView =
                        NativeMethods.FindWindowEx(
                            workerW,
                            IntPtr.Zero,
                            "SHELLDLL_DefView",
                            null);

                } while (
                    shellView == IntPtr.Zero &&
                    workerW != IntPtr.Zero);
            }
        }

        return shellView;
    }
}