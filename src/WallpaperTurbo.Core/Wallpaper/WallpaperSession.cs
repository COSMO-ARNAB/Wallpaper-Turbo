// WallpaperSession.cs - Represents an active wallpaper session in Wallpaper Turbo, managing the media pipeline and associated resources for a specific wallpaper instance.
using System;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Models;

namespace WallpaperTurbo.Core.Wallpaper;

public sealed class WallpaperSession : IDisposable
{
    private bool _disposed;

    public IntPtr WindowHandle { get; }

    public MonitorInfo Monitor { get; private set; }

    public WallpaperEntry Wallpaper { get; }

    public IMediaPipeline MediaPipeline { get; }

    public WallpaperSession(
        IntPtr windowHandle,
        WallpaperEntry wallpaper,
        IMediaPipeline mediaPipeline,
        MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        ArgumentNullException.ThrowIfNull(mediaPipeline);
        ArgumentNullException.ThrowIfNull(monitor);

        WindowHandle = windowHandle;
        Wallpaper = wallpaper;
        MediaPipeline = mediaPipeline;
        Monitor = monitor;
    }

    public void UpdateMonitor(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        Monitor = monitor;
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MediaPipeline.Play();
        MediaPipeline.ApplyLayoutMode(Wallpaper.GetLayoutMode());
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MediaPipeline.Pause();
    }

    /// <summary>Convenience alias for Dispose.</summary>
    public void Shutdown() => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        MediaPipeline.Release();
    }
}