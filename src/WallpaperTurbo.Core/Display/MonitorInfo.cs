namespace WallpaperTurbo.Core.Display;

public sealed class MonitorInfo
{
    public string DeviceName { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsPrimary { get; set; }
}