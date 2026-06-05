using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Models;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Tests;

public class GitHubReleaseProviderChannelValidationTests
{
    private const string ValidSha256 = "1111111111111111111111111111111111111111111111111111111111111111";

    private static RemoteUpdateManifest BuildRemote(string channel) => new()
    {
        SchemaVersion = 1,
        GeneratedAt = "2026-06-05T00:00:00Z",
        Version = "2.0.0",
        Channel = channel,
        ReleaseNotes = "test fixture",
        InstallerFilename = "Wallpaper_Turbo_Setup.exe",
        DownloadUrl = "https://example.invalid/setup.exe",
        Sha256 = ValidSha256,
        FileSizeBytes = 1000,
        MinSupportedVersion = "1.0.0",
        MinSignatureRequired = "sha256-only",
        RollbackEligible = false,
    };

    [Fact]
    public void MapRemoteManifest_ChannelClaimStableButReleaseIsPreview_ReturnsNull()
    {
        // GitHub's `prerelease` flag → releaseChannel=Preview. The update.json
        // claims channel=stable. The publisher's claim must not override the
        // authoritative GitHub flag (Issue 2).
        var remote = BuildRemote(channel: "stable");
        var result = GitHubReleaseProvider.MapRemoteManifestToUpdateManifest(remote, ReleaseChannel.Preview);
        Assert.Null(result);
    }

    [Fact]
    public void MapRemoteManifest_ChannelClaimPreviewButReleaseIsStable_ReturnsNull()
    {
        // Inverse direction: GitHub flag → releaseChannel=Stable. The update.json
        // claims channel=preview. Same rejection logic.
        var remote = BuildRemote(channel: "preview");
        var result = GitHubReleaseProvider.MapRemoteManifestToUpdateManifest(remote, ReleaseChannel.Stable);
        Assert.Null(result);
    }

    [Fact]
    public void MapRemoteManifest_ChannelClaimAgreesWithReleaseChannel_ReturnsMapped()
    {
        // Happy path: claim matches authoritative value. Must succeed.
        var remote = BuildRemote(channel: "preview");
        var result = GitHubReleaseProvider.MapRemoteManifestToUpdateManifest(remote, ReleaseChannel.Preview);
        Assert.NotNull(result);
        Assert.Equal(ReleaseChannel.Preview, result!.Channel);
        Assert.Equal(new SemanticVersion(2, 0, 0), result.Version);
    }

    [Fact]
    public void MapRemoteManifest_StableChannelClaimAgreesWithStableRelease_ReturnsMapped()
    {
        var remote = BuildRemote(channel: "stable");
        var result = GitHubReleaseProvider.MapRemoteManifestToUpdateManifest(remote, ReleaseChannel.Stable);
        Assert.NotNull(result);
        Assert.Equal(ReleaseChannel.Stable, result!.Channel);
    }
}
