using System;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Media.Pipelines;
using WallpaperTurbo.Core.Display;

namespace WallpaperTurbo.Core.Wallpaper;

public sealed class WallpaperSession
{
    public IntPtr WindowHandle { get; }

    public MonitorInfo Monitor { get; }

    public object Wallpaper { get; }

    public IMediaPipeline MediaPipeline { get; }

    public WallpaperSession(
        IntPtr windowHandle,
        object wallpaper,
        IMediaPipeline mediaPipeline,
        MonitorInfo monitor)
    {
        WindowHandle = windowHandle;
        Wallpaper = wallpaper;
        MediaPipeline = mediaPipeline;
        Monitor = monitor;
    }

    public void Play()
    {
        MediaPipeline.Play();
    }

    public void Pause()
    {
        MediaPipeline.Pause();
    }

    public void Shutdown()
    {
        MediaPipeline.Release();
    }
}