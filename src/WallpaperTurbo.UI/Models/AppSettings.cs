namespace WallpaperTurbo.UI.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Layout { get; set; } = "Minimal";
    public bool PauseOnMaximized { get; set; } = true;
    public bool MuteAudio { get; set; } = true;
    public string GpuPreference { get; set; } = "Default";
}
