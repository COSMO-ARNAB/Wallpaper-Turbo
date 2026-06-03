using System;

namespace WallpaperTurbo.Core.Updates.Models;

public record UpdateManifest(
    Version Version,
    ReleaseChannel Channel,
    string ReleaseNotes,
    string DownloadUrl,
    string Sha256Hash,
    long FileSizeBytes,
    Version MinSupportedVersion,
    bool IsRollbackEligible
);
