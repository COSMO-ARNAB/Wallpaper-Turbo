namespace WallpaperTurbo.Core.Updates.Models;

public record UpdateProgress(
    long BytesDownloaded,
    long TotalBytes,
    double PercentComplete
);
