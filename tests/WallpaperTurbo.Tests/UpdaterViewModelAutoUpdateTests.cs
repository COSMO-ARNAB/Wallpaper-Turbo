using System;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.Tests;

public sealed class UpdaterViewModelAutoUpdateTests
{
    [Fact]
    public async Task RunStartupCheck_WhenAutoUpdatesDisabled_SkipsCoordinatorCheck()
    {
        var fixture = new UpdaterFixture(
            new UpdaterSettings
            {
                AutoUpdateEnabled = false,
                CheckOnStartup = true,
                ReleaseChannel = ReleaseChannel.Stable
            });

        await fixture.ViewModel.RunStartupCheckAsync();

        Assert.Equal(0, fixture.UpdateService.CallCount);
        Assert.Equal(UpdateState.Idle, fixture.Coordinator.CurrentState);
    }

    [Fact]
    public async Task RunStartupCheck_WhenAutoUpdatesEnabled_UsesCoordinatorCheck()
    {
        var fixture = new UpdaterFixture(
            new UpdaterSettings
            {
                AutoUpdateEnabled = true,
                CheckOnStartup = true,
                ReleaseChannel = ReleaseChannel.Stable
            });

        await fixture.ViewModel.RunStartupCheckAsync();

        Assert.Equal(1, fixture.UpdateService.CallCount);
        Assert.Equal(UpdateState.UpToDate, fixture.Coordinator.CurrentState);
    }

    private sealed class FakeUpdaterSettingsStore : IUpdaterSettingsStore
    {
        private UpdaterSettings _settings;

        public FakeUpdaterSettingsStore(UpdaterSettings settings)
        {
            _settings = settings.Clone();
        }

        public UpdaterSettings Load() => _settings.Clone();

        public void Save(UpdaterSettings settings)
        {
            _settings = settings.Clone();
        }
    }

    private sealed class CountingUpdateService : IUpdateService
    {
        public int CallCount { get; private set; }

        public Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool IsAvailable, UpdateManifest? Manifest)>((false, null));
        }
    }

    private sealed class UpdaterFixture
    {
        public CountingUpdateService UpdateService { get; } = new();
        public UpdateCoordinator Coordinator { get; }
        public UpdaterViewModel ViewModel { get; }

        public UpdaterFixture(UpdaterSettings settings)
        {
            Coordinator = new UpdateCoordinator(
                UpdateService,
                new NoOpDownloadManager(),
                new AlwaysValidSignatureValidator(),
                new NoOpUpdateApplier(),
                new NoOpProcessManager());

            ViewModel = new UpdaterViewModel(Coordinator, new FakeUpdaterSettingsStore(settings));
        }
    }

    private sealed class NoOpDownloadManager : IDownloadManager
    {
        public Task<string> DownloadUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(destinationPath);
    }

    private sealed class AlwaysValidSignatureValidator : ISignatureValidator
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
