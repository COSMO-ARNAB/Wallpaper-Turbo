namespace WallpaperTurbo.Core.Updates.Models;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    Downloaded,
    Verifying,
    ReadyToInstall,
    ShuttingDown,
    Installing,
    Failed
}
