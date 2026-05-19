//WindowsCapabilityDetector.cs - Provides functionality to detect if the desktop composition has been raised in Windows, which is relevant for determining the appropriate strategy for attaching windows to the desktop in Wallpaper Turbo.
using System;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public static class WindowsCapabilityDetector
{
    public static bool HasRaisedDesktopComposition()
    {
        IntPtr progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

        if (progman == IntPtr.Zero)
            return false;

        int exStyle =
            NativeMethods.GetWindowLong(
                progman,
                NativeMethods.GWL_EXSTYLE);

        return ((uint)exStyle &
            (uint)NativeMethods.WindowStyles.WS_EX_NOREDIRECTIONBITMAP) != 0;
    }
}