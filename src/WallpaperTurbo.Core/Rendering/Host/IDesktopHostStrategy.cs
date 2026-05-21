// IDesktopHostStrategy.cs

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

    void Detach(
        IntPtr hwnd);
}