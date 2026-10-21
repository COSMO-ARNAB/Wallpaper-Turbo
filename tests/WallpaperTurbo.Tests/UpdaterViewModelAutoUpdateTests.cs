using System;
using System.Diagnostics;
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

    [Fact]
    public async Task RunStartupCheck_WhenCheckHangs_ReturnsAfterTimeout()
    {
        // A coordinator whose check never completes; the view model must stop
        // waiting after its timeout instead of stalling startup follow-ups.
        var hangingCoordinator = new UpdateCoordinator(
            new HangingUpdateService(),
            new NoOpDownloadManager(),
            new AlwaysValidSignatureValidator(),
            new NoOpUpdateApplier(),
            new NoOpProcessManager());
        var settingsStore = new FakeUpdaterSettingsStore(new UpdaterSettings
        {
            AutoUpdateEnabled = true,
            CheckOnStartup = true,
            ReleaseChannel = ReleaseChannel.Stable
        });
        var viewModel = new UpdaterViewModel(hangingCoordinator, settingsStore)
        {
            StartupCheckTimeout = TimeSpan.FromMilliseconds(200)
        };

        var sw = Stopwatch.StartNew();
        await viewModel.RunStartupCheckAsync();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"Startup check with a hung backend must return after the timeout, took {sw.Elapsed}.");
    }

    private sealed class HangingUpdateService : IUpdateService
    {
        public Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            // Never completes: simulates a stalled network with no usable HttpClient timeout.
            return new TaskCompletionSource<(bool IsAvailable, UpdateManifest? Manifest)>().Task;
        }
    }

    [Fact]
    public async Task RunStartupCheck_WhenSkipStartupCheckOnceIsSet_SkipsOnceThenClearsFlag()
    {
        var fixture = new UpdaterFixture(
            new UpdaterSettings
            {
                AutoUpdateEnabled = true,
                CheckOnStartup = true,
                SkipStartupCheckOnce = true,
                ReleaseChannel = ReleaseChannel.Stable
            });

        await fixture.ViewModel.RunStartupCheckAsync();

        Assert.Equal(0, fixture.UpdateService.CallCount);
        Assert.False(fixture.ViewModel.GetSettingsSnapshot().SkipStartupCheckOnce);
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
