using System;
using WallpaperTurbo.Core.Rendering;

namespace WallpaperTurbo.Core.Wallpaper;

public interface IWallpaperManager
{
    void InitializeDesktopHandle();

    IntPtr WorkerWHandle { get; }

    void AttachWindow(IntPtr childWindowHandle);
}

public sealed class WindowsWallpaperManager
    : IWallpaperManager
{
    public IntPtr WorkerWHandle =>
        DesktopAttachmentService.WorkerWHandle;

    public WindowsWallpaperManager(
        Action<string>? logger = null)
    {
    }

    public void InitializeDesktopHandle()
    {
        DesktopAttachmentService.Initialize();
    }

    public void AttachWindow(
        IntPtr childWindowHandle)
    {
        DesktopAttachmentService.Attach(
            childWindowHandle);
    }
}