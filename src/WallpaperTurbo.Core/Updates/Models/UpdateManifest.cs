using System;

namespace WallpaperTurbo.Core.Updates.Models;

public sealed record UpdateManifest(
    SemanticVersion Version,
    ReleaseChannel Channel,
    string ReleaseNotes,
    string DownloadUrl,
    string Sha256Hash,
    long FileSizeBytes,
    SemanticVersion MinSupportedVersion,
    bool IsRollbackEligible
);
