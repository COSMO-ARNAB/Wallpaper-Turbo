//DesktopAttachmentService.cs - Contains logic to find the WorkerW window and attach our wallpaper rendering surface to it for desktop integration
/*using System;
using System.Runtime.InteropServices;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering;

public static class DesktopAttachmentService
{
    public static IntPtr GetWorkerW()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);

        NativeMethods.SendMessageTimeout(
            progman,
            0x052C,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            1000,
            out _
        );

        IntPtr workerw = IntPtr.Zero;

        NativeMethods.EnumWindows((topHandle, _) =>
        {
            IntPtr shellView = NativeMethods.FindWindowEx(
                topHandle,
                IntPtr.Zero,
                "SHELLDLL_DefView",
                null
            );

            if (shellView != IntPtr.Zero)
            {
                workerw = NativeMethods.FindWindowEx(
                    IntPtr.Zero,
                    topHandle,
                    "WorkerW",
                    null
                );
            }

            return true;
        }, IntPtr.Zero);

        return workerw;
    }

    public static void AttachToDesktop(IntPtr wallpaperHandle)
    {
        IntPtr workerw = GetWorkerW();

        if (workerw == IntPtr.Zero)
        {
            throw new Exception("WorkerW not found.");
        }

        NativeMethods.SetParent(wallpaperHandle, workerw);
    }
}*/