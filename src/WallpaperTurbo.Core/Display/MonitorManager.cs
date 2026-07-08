// MonitorManager.cs - Provides functionality to retrieve information about connected monitors in Wallpaper Turbo.
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace WallpaperTurbo.Core.Display;

public static class MonitorManager
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        foreach (var screen in Screen.AllScreens)
        {
            monitors.Add(new MonitorInfo
            {
                DeviceName = screen.DeviceName,
                X = screen.Bounds.X,
                Y = screen.Bounds.Y,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                IsPrimary = screen.Primary
            });
        }

        return monitors;
    }

    public static MonitorInfo GetPrimaryMonitor()
    {
        MonitorInfo? firstMonitor = null;

        foreach (var monitor in GetMonitors())
        {
            firstMonitor ??= monitor;

            if (monitor.IsPrimary)
                return monitor;
        }

        return firstMonitor ?? throw new InvalidOperationException(
            "No monitors found.");
    }
}
