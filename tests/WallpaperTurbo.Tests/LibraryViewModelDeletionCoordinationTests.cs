using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;
using WallpaperTurbo.Updater;
using WallpaperTurbo.Updater.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

public class LibraryViewModelDeletionCoordinationTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public event EventHandler<AppSettings>? SettingsChanged;
        public AppSettings Load() => new AppSettings();
        public void Save(AppSettings settings) { }
    }

    private sealed class FakeGpuPreferenceService : IGpuPreferenceService
    {
        public void SetGpuPreference(string exePath, GpuPreference mode) { }
        public GpuPreference GetGpuPreference(string exePath) => GpuPreference.Auto;
    }

    private sealed class FakeWallpaperLibraryService : IWallpaperLibraryService
    {
        public event EventHandler<WallpaperEntry>? MetadataChanged;
        public List<WallpaperEntry> Wallpapers { get; } = new();

        public Task<IReadOnlyList<WallpaperEntry>> GetWallpapersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WallpaperEntry>>(Wallpapers);

        public Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default, IProgress<ImportProgress>? progress = null)
        {
            var entry = new WallpaperEntry { Id = Guid.NewGuid().ToString(), Title = "Wp", Video = sourceFilePath };
            Wallpapers.Add(entry);
            return Task.FromResult(entry);
        }

        public Task ShutdownAsync() => Task.CompletedTask;
        public Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default)
        {
            var wp = Wallpapers.FirstOrDefault(w => w.Id == guid);
            if (wp != null)
            {
                Wallpapers.Remove(wp);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        public Task<bool> UpdateWallpaperMetadataAsync(string guid, string? title, string? author, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeLayoutPreferenceStore : ILayoutPreferenceStore
    {
        public LayoutMode GetSavedLayout() => LayoutMode.Minimal;
        public void SaveLayout(LayoutMode layoutMode) { }
    }

    private sealed class FakeUpdaterSettingsStore : IUpdaterSettingsStore
    {
        private UpdaterSettings _settings = new();
        public UpdaterSettings Load() => _settings.Clone();
        public void Save(UpdaterSettings settings) => _settings = settings.Clone();
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(ReleaseChannel channel, CancellationToken cancellationToken = default)
            => Task.FromResult<(bool IsAvailable, UpdateManifest? Manifest)>((false, null));
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
        public void ApplyUpdate(string installerFilePath) { }
    }

    private sealed class NoOpProcessManager : IProcessManager
    {
        public Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds) => Task.FromResult(true);
        public void ShutdownCurrentProcessGracefully() { }
    }

    [Fact]
    public async Task DeleteWallpaperAsync_WhenLastWallpaperDeleted_ResetsDashboardAndMainViewModel()
    {
        var services = new ServiceCollection();
        var libraryService = new FakeWallpaperLibraryService();
        var settingsStore = new FakeSettingsStore();
        var gpuService = new FakeGpuPreferenceService();

        var wp = new WallpaperEntry { Id = "1", Title = "Active Wallpaper", Video = "wp1.mp4" };
        libraryService.Wallpapers.Add(wp);

        var wallpaperService = new WallpaperService(libraryService, settingsStore, gpuService);
        var telemetryService = new TelemetryService();
        var diagnosticsService = new DiagnosticsService();

        services.AddSingleton<IWallpaperLibraryService>(libraryService);
        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddSingleton<IGpuPreferenceService>(gpuService);
        services.AddSingleton(wallpaperService);
        services.AddSingleton(telemetryService);
        services.AddSingleton(diagnosticsService);

        var layoutHostVm = new LayoutHostViewModel(new FakeLayoutPreferenceStore());
        var updaterVm = new UpdaterViewModel(
            new UpdateCoordinator(
                new FakeUpdateService(),
                new NoOpDownloadManager(),
                new AlwaysValidSignatureValidator(),
                new NoOpUpdateApplier(),
                new NoOpProcessManager()),
            new FakeUpdaterSettingsStore());

        var dashboardVm = new DashboardViewModel(wallpaperService, diagnosticsService, settingsStore);
        var libraryVm = new LibraryViewModel(wallpaperService);
        var settingsVm = new SettingsViewModel(wallpaperService, updaterVm, layoutHostVm, settingsStore);
        var presentation = new PresentationManager(wallpaperService, settingsStore);

        var mainVm = new MainViewModel(
            wallpaperService,
            telemetryService,
            libraryService,
            settingsStore,
            updaterVm,
            dashboardVm,
            libraryVm,
            settingsVm,
            layoutHostVm,
            presentation);

        services.AddSingleton(mainVm);
        services.AddSingleton(dashboardVm);
        services.AddSingleton(libraryVm);

        var serviceProvider = services.BuildServiceProvider();
        App.SetTestServiceProvider(serviceProvider);

        // Load initially
        await dashboardVm.LoadLibraryAsync();
        await libraryVm.LoadLibraryAsync();

        // Pretend this wallpaper is currently active in MainViewModel
        mainVm.SetActiveWallpaperInfo(wp.Title, "3840 x 2160 • 60 FPS");
        dashboardVm.ActiveWallpaper = wp;
        dashboardVm.LastDisplayedWallpaper = wp;
        dashboardVm.RecentlyUsedWallpapers.Add(wp);

        // Trigger deletion
        Assert.True(libraryVm.DeleteWallpaperCommand.CanExecute(wp));
        var task = libraryVm.DeleteWallpaperCommand.ExecuteAsync(wp);

        // Accept the dialog confirm in MainViewModel
        Assert.True(mainVm.IsDialogVisible);
        Assert.Equal("Confirm Delete", mainVm.DialogTitle);
        var confirmCommand = Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(mainVm.DialogConfirmCommand);
        await confirmCommand.ExecuteAsync(null);

        await task;

        // Verify that:
        // 1. Wallpaper is removed from library list
        Assert.Empty(libraryVm.FilteredWallpapers);

        // 2. Active wallpaper info in MainViewModel is reset
        Assert.Equal("No Active Wallpaper", mainVm.ActiveWallpaperTitle);

        // 3. Featured sections / recently used / active wallpaper in DashboardViewModel are cleared
        Assert.Null(dashboardVm.HeroWallpaper);
        Assert.Null(dashboardVm.ActiveWallpaper);
        Assert.Null(dashboardVm.LastDisplayedWallpaper);
        Assert.Empty(dashboardVm.RecentlyUsedWallpapers);

        // 4. Telemetry preview values are reset
        Assert.Equal(0, dashboardVm.GpuValue);
        Assert.Equal(0, dashboardVm.CpuValue);
        Assert.Equal(0, dashboardVm.VideoDecodeValue);
        Assert.StartsWith("0.0 /", dashboardVm.RamValueText);
        Assert.StartsWith("0.0 /", dashboardVm.VramValueText);
    }
}
