using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater;

namespace WallpaperTurbo.Tests;

public class UpdateCoordinatorRecheckTests
{
    [Fact]
    public async Task CheckForUpdates_WhenAlreadyUpdateAvailable_AllowsManualRecheck()
    {
        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = new UpdateManifest(
            Version: new SemanticVersion(2, 0, 0),
            Channel: ReleaseChannel.Stable,
            ReleaseNotes: "test",
            DownloadUrl: "https://example.invalid/setup.exe",
            Sha256Hash: "1111111111111111111111111111111111111111111111111111111111111111",
            FileSizeBytes: 1000,
            MinSupportedVersion: new SemanticVersion(1, 0, 0),
            IsRollbackEligible: false,
            MinSignatureRequirement: SignatureRequirement.Authenticode);

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);

        Assert.Equal(UpdateState.UpdateAvailable, fixture.Coordinator.CurrentState);

        fixture.UpdateService.CallCount = 0;
        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);

        Assert.Equal(1, fixture.UpdateService.CallCount);
        Assert.Equal(UpdateState.UpdateAvailable, fixture.Coordinator.CurrentState);
    }

    private sealed class CoordinatorFixture
    {
        public FakeUpdateService UpdateService { get; } = new();
        public UpdateCoordinator Coordinator { get; }

        public CoordinatorFixture()
        {
            Coordinator = new UpdateCoordinator(
                UpdateService,
                new NoOpDownloadManager(),
                new NoOpSignatureValidator(),
                new NoOpUpdateApplier(),
                new NoOpProcessManager());
        }
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public int CallCount { get; set; }
        public UpdateManifest? ManifestToReturn { get; set; }

        public Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult((ManifestToReturn != null, ManifestToReturn));
        }
    }

    private sealed class NoOpDownloadManager : IDownloadManager
    {
        public Task<string> DownloadUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(destinationPath);
    }

    private sealed class NoOpSignatureValidator : ISignatureValidator
    {
        public bool IsValidSignature(string filePath) => true;
    }

    private sealed class NoOpUpdateApplier : IUpdateApplier
    {
        public void ApplyUpdate(string installerFilePath)
        {
        }
    }

    private sealed class NoOpProcessManager : IProcessManager
    {
        public Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds) => Task.FromResult(true);
        public void ShutdownCurrentProcessGracefully()
        {
        }
    }
}
