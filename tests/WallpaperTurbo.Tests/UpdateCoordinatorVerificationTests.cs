using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater;
using WallpaperTurbo.Updater.Events;

namespace WallpaperTurbo.Tests;

public class UpdateCoordinatorVerificationTests
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

    private static UpdateManifest MakeManifest(
        string sha256,
        SignatureRequirement sig,
        ReleaseChannel channel,
        SemanticVersion version)
    {
        return new UpdateManifest(
            Version: version,
            Channel: channel,
            ReleaseNotes: "test fixture",
            DownloadUrl: "https://example.invalid/setup.exe",
            Sha256Hash: sha256,
            FileSizeBytes: TestBytes.Length,
            MinSupportedVersion: new SemanticVersion(1, 0, 0),
            IsRollbackEligible: false,
            MinSignatureRequirement: sig);
    }

    [Fact]
    public async Task Verify_PreviewReleaseWithEmptySha256_RejectsUpdate()
    {
        var manifest = MakeManifest(
            sha256: string.Empty,
            sig: SignatureRequirement.Sha256Only,
            channel: ReleaseChannel.Preview,
            version: new SemanticVersion(2, 0, 0, "rc.1"));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = manifest;
        fixture.DownloadManager.BytesToWrite = TestBytes;

        UpdateErrorEventArgs? capturedError = null;
        fixture.Coordinator.ErrorOccurred += (_, e) => capturedError = e;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Preview);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.NotNull(capturedError);
        Assert.Contains("SHA256", capturedError!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UpdateState.Failed, fixture.Coordinator.CurrentState);
        Assert.False(fixture.Applier.ApplyWasCalled);
        Assert.NotNull(fixture.DownloadManager.LastDestinationPath);
        Assert.False(File.Exists(fixture.DownloadManager.LastDestinationPath!),
            "Empty-SHA256 rejection must clean up the partially downloaded file (Issue 3c).");
    }

    [Fact]
    public async Task Verify_PreviewReleaseWithValidSha256AndSha256Only_ReachesReadyToInstall()
    {
        var sha256 = ComputeSha256Hex(TestBytes);
        var manifest = MakeManifest(
            sha256: sha256,
            sig: SignatureRequirement.Sha256Only,
            channel: ReleaseChannel.Preview,
            version: new SemanticVersion(2, 0, 0, "rc.1"));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = manifest;
        fixture.DownloadManager.BytesToWrite = TestBytes;

        UpdateErrorEventArgs? capturedError = null;
        fixture.Coordinator.ErrorOccurred += (_, e) => capturedError = e;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Preview);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.Null(capturedError);
        Assert.Equal(UpdateState.ReadyToInstall, fixture.Coordinator.CurrentState);
    }

    [Fact]
    public async Task Verify_StableReleaseWithValidSha256AndValidAuthenticode_ReachesReadyToInstall()
    {
        var sha256 = ComputeSha256Hex(TestBytes);
        var manifest = MakeManifest(
            sha256: sha256,
            sig: SignatureRequirement.Authenticode,
            channel: ReleaseChannel.Stable,
            version: new SemanticVersion(2, 0, 0));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = manifest;
        fixture.DownloadManager.BytesToWrite = TestBytes;
        fixture.SignatureValidator.ReturnValue = true;

        UpdateErrorEventArgs? capturedError = null;
        fixture.Coordinator.ErrorOccurred += (_, e) => capturedError = e;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.Null(capturedError);
        Assert.Equal(UpdateState.ReadyToInstall, fixture.Coordinator.CurrentState);
    }

    [Fact]
    public async Task Verify_StableReleaseWithValidSha256ButInvalidAuthenticode_RejectsUpdate()
    {
        var sha256 = ComputeSha256Hex(TestBytes);
        var manifest = MakeManifest(
            sha256: sha256,
            sig: SignatureRequirement.Authenticode,
            channel: ReleaseChannel.Stable,
            version: new SemanticVersion(2, 0, 0));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = manifest;
        fixture.DownloadManager.BytesToWrite = TestBytes;
        fixture.SignatureValidator.ReturnValue = false;

        UpdateErrorEventArgs? capturedError = null;
        fixture.Coordinator.ErrorOccurred += (_, e) => capturedError = e;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.NotNull(capturedError);
        Assert.Contains("signature", capturedError!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UpdateState.Failed, fixture.Coordinator.CurrentState);
        Assert.False(fixture.Applier.ApplyWasCalled);
    }

    [Fact]
    public async Task Verify_PreviewReleaseWithMismatchedSha256_RejectsUpdate()
    {
        var sha256 = ComputeSha256Hex(TestBytes);
        var manifest = MakeManifest(
            sha256: sha256,
            sig: SignatureRequirement.Sha256Only,
            channel: ReleaseChannel.Preview,
            version: new SemanticVersion(2, 0, 0, "rc.1"));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = manifest;
        fixture.DownloadManager.BytesToWrite = new byte[] { 0xFF, 0xFE, 0xFD };

        UpdateErrorEventArgs? capturedError = null;
        fixture.Coordinator.ErrorOccurred += (_, e) => capturedError = e;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Preview);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.NotNull(capturedError);
        Assert.Contains("hash", capturedError!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UpdateState.Failed, fixture.Coordinator.CurrentState);
        Assert.False(fixture.Applier.ApplyWasCalled);
    }

    [Fact]
    public async Task Verify_StableUserWithManifestClaimingSha256Only_ReachesReadyToInstall()
    {
        // v1.2.5: The Stable channel now uses Sha256Only as its signature floor
        // (matching Preview/Nightly and the build-update-manifest.ps1 default).
        // A Stable user receiving a manifest that claims Sha256Only must
        // therefore reach ReadyToInstall, exactly like the Preview case below.
        var sha256 = ComputeSha256Hex(TestBytes);
        var manifest = MakeManifest(
            sha256: sha256,
            sig: SignatureRequirement.Sha256Only,
            channel: ReleaseChannel.Stable,
            version: new SemanticVersion(2, 0, 0));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = manifest;
        fixture.DownloadManager.BytesToWrite = TestBytes;

        UpdateErrorEventArgs? capturedError = null;
        fixture.Coordinator.ErrorOccurred += (_, e) => capturedError = e;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Stable);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.Null(capturedError);
        Assert.Equal(UpdateState.ReadyToInstall, fixture.Coordinator.CurrentState);
    }

    [Fact]
    public async Task Verify_PreviewUserWithManifestClaimingSha256Only_ReachesReadyToInstall()
    {
        // Happy-path for the user-channel-minimum check: Preview users accept
        // sha256-only (their channel default). This is the current pre-Fix-1
        // behavior, and the test guards against regression.
        var sha256 = ComputeSha256Hex(TestBytes);
        var manifest = MakeManifest(
            sha256: sha256,
            sig: SignatureRequirement.Sha256Only,
            channel: ReleaseChannel.Preview,
            version: new SemanticVersion(2, 0, 0, "rc.1"));

        var fixture = new CoordinatorFixture();
        fixture.UpdateService.ManifestToReturn = manifest;
        fixture.DownloadManager.BytesToWrite = TestBytes;

        UpdateErrorEventArgs? capturedError = null;
        fixture.Coordinator.ErrorOccurred += (_, e) => capturedError = e;

        await fixture.Coordinator.CheckForUpdatesAsync(ReleaseChannel.Preview);
        await fixture.Coordinator.DownloadUpdateAsync();

        Assert.Null(capturedError);
        Assert.Equal(UpdateState.ReadyToInstall, fixture.Coordinator.CurrentState);
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
            return Task.FromResult(destinationPath);
        }
    }

    private sealed class FakeSignatureValidator : ISignatureValidator
    {
        public bool ReturnValue = true;
        public bool IsValidSignature(string filePath) => ReturnValue;
    }

    private sealed class FakeUpdateApplier : IUpdateApplier
    {
        public bool ApplyWasCalled;
        public void ApplyUpdate(string installerFilePath) => ApplyWasCalled = true;
    }

    private sealed class FakeProcessManager : IProcessManager
    {
        public Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds) => Task.FromResult(true);
        public void ShutdownCurrentProcessGracefully() { }
    }

    private sealed class CoordinatorFixture
    {
        public FakeUpdateService UpdateService { get; } = new();
        public FakeDownloadManager DownloadManager { get; } = new();
        public FakeSignatureValidator SignatureValidator { get; } = new();
        public FakeUpdateApplier Applier { get; } = new();
        public FakeProcessManager ProcessManager { get; } = new();
        public UpdateCoordinator Coordinator { get; }

        public CoordinatorFixture()
        {
            Coordinator = new UpdateCoordinator(
                UpdateService,
                DownloadManager,
                SignatureValidator,
                Applier,
                ProcessManager);
        }
    }
}
