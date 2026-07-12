namespace WallpaperTurbo.Core.Updates.Models;

public sealed class UpdaterSettings
{
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool CheckOnStartup { get; set; } = true;
    public bool SkipStartupCheckOnce { get; set; }
    public ReleaseChannel ReleaseChannel { get; set; } = ReleaseChannel.Stable;

    public UpdaterSettings Clone()
    {
        return new UpdaterSettings
        {
            AutoUpdateEnabled = AutoUpdateEnabled,
            CheckOnStartup = CheckOnStartup,
            SkipStartupCheckOnce = SkipStartupCheckOnce,
            ReleaseChannel = ReleaseChannel
        };
    }
}
