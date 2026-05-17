using System;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Media.Pipelines;

namespace WallpaperTurbo.Core.Wallpaper;

public sealed class WallpaperSession
{
    public IntPtr WindowHandle { get; }

    public object Wallpaper { get; }

    public IMediaPipeline MediaPipeline { get; }

    public WallpaperSession(
        IntPtr windowHandle,
        object wallpaper,
        IMediaPipeline mediaPipeline)
    {
        WindowHandle = windowHandle;
        Wallpaper = wallpaper;
        MediaPipeline = mediaPipeline;
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