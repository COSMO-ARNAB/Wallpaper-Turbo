using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater;

namespace WallpaperTurbo.Tests;

public class UpdateCoordinatorDownloadRetentionTests
{
    private static readonly byte[] TestBytes = new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10
    };

    private static string ComputeSha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static UpdateManifest MakeManifest(SemanticVersion version)
    {
        return new UpdateManifest(
            Version: version,
            Channel: ReleaseChannel.Stable,
            ReleaseNotes: "test fixture",
            DownloadUrl: "https://example.invalid/setup.exe",
            Sha256Hash: ComputeSha256Hex(TestBytes),
            FileSizeBytes: TestBytes.Length,
            MinSupportedVersion: new SemanticVersion(1, 0, 0),
            IsRollbackEligible: false,
            MinSignatureRequirement: SignatureRequirement.Sha256Only);
    }

    [Fact]
    public async Task Download_WhenDownloadFails_KeepsPartialFileForResume()
    {
        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = MakeManifest(new SemanticVersion(3, 1, 0));
        fixture.DownloadManager.BytesToWrite = TestBytes[..6];
        fixture.DownloadManager.ExceptionToThrow = new IOException("simulated network failure");

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.Equal(UpdateState.Failed, fixture.Coordinator.CurrentState);
        Assert.NotNull(fixture.DownloadManager.LastDestinationPath);
        Assert.True(File.Exists(fixture.DownloadManager.LastDestinationPath!),
            "Download failure must keep the partially downloaded file so the next attempt can resume.");
    }

    [Fact]
    public async Task Download_WhenCancelled_KeepsPartialFileForResume()
    {
        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = MakeManifest(new SemanticVersion(3, 2, 0));
        fixture.DownloadManager.BytesToWrite = TestBytes[..6];
        fixture.DownloadManager.ExceptionToThrow = new OperationCanceledException();

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.Equal(UpdateState.UpdateAvailable, fixture.Coordinator.CurrentState);
        Assert.NotNull(fixture.DownloadManager.LastDestinationPath);
        Assert.True(File.Exists(fixture.DownloadManager.LastDestinationPath!),
            "Cancelled download must keep the partially downloaded file so the next attempt can resume.");
    }

    [Fact]
    public async Task Download_WhenSuccessful_RemovesStaleOlderVersionFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WallpaperTurboUpdates");
        Directory.CreateDirectory(tempDir);
        var stalePath = Path.Combine(tempDir, "WallpaperTurbo_Update_1.0.0.exe");
        File.WriteAllBytes(stalePath, new byte[] { 0xAA });
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow - TimeSpan.FromDays(1));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = MakeManifest(new SemanticVersion(3, 3, 0));
        fixture.DownloadManager.BytesToWrite = TestBytes;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.Equal(UpdateState.ReadyToInstall, fixture.Coordinator.CurrentState);
        Assert.False(File.Exists(stalePath), "Successful download must remove stale older-version installer files.");
        Assert.True(File.Exists(fixture.DownloadManager.LastDestinationPath!),
            "Successful download must keep the current version installer file.");
    }

    // -----------------------------------------------------------------------
    // Test fakes
    // -----------------------------------------------------------------------
    private sealed class FakeUpdateService : IUpdateService
    {
        public UpdateManifest? ManifestToReturn;
        public Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((ManifestToReturn != null, ManifestToReturn));
        }
    }

    private sealed class FakeDownloadManager : IDownloadManager
    {
        public byte[] BytesToWrite = Array.Empty<byte>();
        public Exception? ExceptionToThrow;
        public string? LastDestinationPath { get; private set; }

        public Task<string> DownloadUpdateAsync(
            UpdateManifest manifest,
            string destinationPath,
            IProgress<UpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(destinationPath, BytesToWrite);
            LastDestinationPath = destinationPath;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(destinationPath);
        }
    }

    private sealed class FakeSignatureValidator : ISignatureValidator
    {
        public bool IsValidSignature(string filePath) => true;
    }

    private sealed class FakeUpdateApplier : IUpdateApplier
    {
        public void ApplyUpdate(string installerFilePath) { }
    }

    private sealed class FakeProcessManager : IProcessManager
    {
        public Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds)
            => Task.FromResult(true);

        public void ShutdownCurrentProcessGracefully() { }
    }

    private sealed class CoordinatorFixture
    {
        public FakeUpdateService UpdateService { get; } = new();
        public FakeDownloadManager DownloadManager { get; } = new();
        public UpdateCoordinator Coordinator { get; }

        public CoordinatorFixture()
        {
            Coordinator = new UpdateCoordinator(
                UpdateService,
                DownloadManager,
                new FakeSignatureValidator(),
                new FakeUpdateApplier(),
                new FakeProcessManager());
        }
    }
}
