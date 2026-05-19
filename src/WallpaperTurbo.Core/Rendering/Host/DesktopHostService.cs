// DesktopHostService.cs - Service for attaching windows to the desktop using various strategies in Wallpaper Turbo.
using System;
using System.Collections.Generic;
using WallpaperTurbo.Core.Display;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class DesktopHostService
{
    private readonly List<IDesktopHostStrategy>
        _strategies;

    public DesktopHostService()
    {
        _strategies =
        [
            new DesktopCompositionStrategy(),
            new WorkerWStrategy()
        ];
    }

    public bool Attach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        foreach (var strategy in _strategies)
        {
            try
            {
                if (!strategy.IsSupported())
                {
                    Console.WriteLine(
                        $"[Host] Skipping unsupported strategy: {strategy.Name}");

                    continue;
                }

                Console.WriteLine(
                    $"[Host] Trying strategy: {strategy.Name}");

                bool attached =
                    strategy.TryAttach(
                        hwnd,
                        monitor);

                if (attached)
                {
                    Console.WriteLine(
                        $"[Host] Strategy succeeded: {strategy.Name}");

                    return true;
                }

                Console.WriteLine(
                    $"[Host] Strategy failed: {strategy.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Host] Strategy exception ({strategy.Name}): {ex.Message}");
            }
        }

        return false;
    }
}