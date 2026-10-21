using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Hardware.Models;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;
using WallpaperTurbo.Updater;
using WallpaperTurbo.Updater.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

#pragma warning disable CS0067

public class SurgicalFixesTests
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
        public event EventHandler? WallpapersChanged;
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
    public void NormalizeWallpaperId_MapsCrimsonBlindAndEmptyToFrierenAstral()
    {
        Assert.Equal("frieren-astral", AppSettings.CanonicalDefaultWallpaperId);
        Assert.Equal("frieren-astral", DashboardViewModel.CanonicalDefaultWallpaperId);

        Assert.Equal("frieren-astral", AppSettings.NormalizeWallpaperId("crimson-blind"));
        Assert.Equal("frieren-astral", AppSettings.NormalizeWallpaperId("CRIMSON-BLIND"));
        Assert.Equal("frieren-astral", AppSettings.NormalizeWallpaperId(null));
        Assert.Equal("frieren-astral", AppSettings.NormalizeWallpaperId("   "));
        Assert.Equal("custom-wall", AppSettings.NormalizeWallpaperId("custom-wall"));
    }

    [Theory]
    [InlineData(0.4, 40.0)]
    [InlineData(0.05, 5.0)]
    [InlineData(0.85, 85.0)]
    [InlineData(1.0, 90.0)] // clamped to 90
    [InlineData(0.0, 0.0)]
    [InlineData(40.0, 40.0)]
    [InlineData(90.0, 90.0)]
    [InlineData(100.0, 90.0)] // clamped to 90
    [InlineData(-5.0, 0.0)]   // clamped to 0
    public void NormalizeGlassOpacity_ScalesFractionalAndClampsCorrectly(double input, double expected)
    {
        var result = AppSettings.NormalizeGlassOpacity(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task WallpaperLibraryService_SeedWallpapers_ContainsExpectedPlaylistTags()
    {
        var libService = new WallpaperLibraryService(new DummyThumbnailExtractor());
        var wallpapers = await libService.GetWallpapersAsync();

        Assert.NotEmpty(wallpapers);

        var allPlaylists = wallpapers.SelectMany(w => w.Playlists).Distinct().ToList();
        var allTags = wallpapers.SelectMany(w => w.Tags).Distinct().ToList();

        var combined = allPlaylists.Concat(allTags).ToList();

        Assert.Contains("Favorites", combined);
        Assert.Contains("Anime Vibes", combined);
        Assert.Contains("Cars & Drives", combined);
        Assert.Contains("Dark Aesthetic", combined);
    }

    private sealed class DummyThumbnailExtractor : IThumbnailExtractor
    {
        public Task<string> ExtractThumbnailAsync(string videoPath, string destinationJpgPath, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    [Fact]
    public void LibraryViewModel_ApplyFilter_HandlesCategoriesAndPlaylistsCaseInsensitively()
    {
        var fakeWallpapers = new List<WallpaperEntry>
        {
            new() { Id = "wp1", Title = "4K Anime", Resolution = "3840 x 2160", Tags = new List<string> { "Anime", "4K" }, Playlists = new List<string> { "Anime Vibes", "Favorites" } },
            new() { Id = "wp2", Title = "Sci-fi City", Resolution = "1920 x 1080", Tags = new List<string> { "Sci-Fi" }, Playlists = new List<string> { "Dark Aesthetic" } },
            new() { Id = "wp3", Title = "Car Sunset", Resolution = "2560 x 1440", Tags = new List<string> { "Cars" }, Playlists = new List<string> { "Cars & Drives" } },
        };

        var fakeLib = new FakeWallpaperLibraryService();
        fakeLib.Wallpapers.AddRange(fakeWallpapers);
        var fakeStore = new FakeSettingsStore();
        var fakeGpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(fakeLib, fakeStore, fakeGpu);
        var libVm = new LibraryViewModel(wallpaperService);

        // Verify categories list contains playlists
        Assert.Contains("Favorites", libVm.Categories);
        Assert.Contains("Anime Vibes", libVm.Categories);
        Assert.Contains("Cars & Drives", libVm.Categories);
        Assert.Contains("Dark Aesthetic", libVm.Categories);

        // Verify toggle playlist membership
        libVm.TogglePlaylistMembership("wp1", "Favorites");
        Assert.DoesNotContain("Favorites", fakeWallpapers[0].Playlists);

        libVm.TogglePlaylistMembership("wp1", "Favorites");
        Assert.Contains("Favorites", fakeWallpapers[0].Playlists);
    }

    [Fact]
    public void DashboardViewModel_DisposesCleanly()
    {
        var fakeLib = new FakeWallpaperLibraryService();
        var fakeStore = new FakeSettingsStore();
        var fakeGpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(fakeLib, fakeStore, fakeGpu);
        var vm = new DashboardViewModel(wallpaperService, new DiagnosticsService(), fakeStore);

        // Calling Dispose should not throw
        var exception = Record.Exception(() => vm.Dispose());
        Assert.Null(exception);

        // Multiple calls to Dispose should be idempotent
        var secondException = Record.Exception(() => vm.Dispose());
        Assert.Null(secondException);
    }

    [Fact]
    public void SettingsViewModel_DisposesCleanly()
    {
        var fakeLib = new FakeWallpaperLibraryService();
        var fakeStore = new FakeSettingsStore();
        var fakeGpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(fakeLib, fakeStore, fakeGpu);
        var layoutHostVm = new LayoutHostViewModel(new FakeLayoutPreferenceStore());
        var updaterVm = new UpdaterViewModel(
            new UpdateCoordinator(
                new FakeUpdateService(),
                new NoOpDownloadManager(),
                new AlwaysValidSignatureValidator(),
                new NoOpUpdateApplier(),
                new NoOpProcessManager()),
            new FakeUpdaterSettingsStore());
        var vm = new SettingsViewModel(wallpaperService, updaterVm, layoutHostVm, fakeStore);

        var exception = Record.Exception(() => vm.Dispose());
        Assert.Null(exception);

        var secondException = Record.Exception(() => vm.Dispose());
        Assert.Null(secondException);
    }
}
