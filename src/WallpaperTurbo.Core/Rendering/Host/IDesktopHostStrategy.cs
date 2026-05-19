// IDesktopHostStrategy.cs - Defines the interface for strategies to attach rendering hosts to the desktop in Wallpaper Turbo.
using System;
using WallpaperTurbo.Core.Display;

namespace WallpaperTurbo.Core.Rendering.Host;

public interface IDesktopHostStrategy
{
    string Name { get; }

    bool IsSupported();

    bool TryAttach(
        IntPtr hwnd,
        MonitorInfo monitor);
}