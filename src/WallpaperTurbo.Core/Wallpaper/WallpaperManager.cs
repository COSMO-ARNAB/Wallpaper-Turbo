//WallpaperManager.cs - Manages the attachment of wallpaper windows to the desktop in Wallpaper Turbo.
using System;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Rendering.Host;

namespace WallpaperTurbo.Core.Wallpaper;

public interface IWallpaperManager
{
    bool AttachWindow(
        IntPtr childWindowHandle,
        MonitorInfo monitor);
}

public sealed class WindowsWallpaperManager
    : IWallpaperManager
{
    private readonly DesktopHostService _desktopHostService;

    public WindowsWallpaperManager()
    {
        _desktopHostService = new DesktopHostService();
    }

    public bool AttachWindow(
        IntPtr childWindowHandle,
        MonitorInfo monitor)
    {
        return _desktopHostService.Attach(
            childWindowHandle,
            monitor);
    }
}
