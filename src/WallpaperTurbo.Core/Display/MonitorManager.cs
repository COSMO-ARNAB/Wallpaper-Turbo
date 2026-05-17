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
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                IsPrimary = screen.Primary
            });
        }

        return monitors;
    }
}