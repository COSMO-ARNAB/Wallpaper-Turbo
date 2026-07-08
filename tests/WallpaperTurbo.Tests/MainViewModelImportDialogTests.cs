using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;
using WallpaperTurbo.Updater;
using WallpaperTurbo.Updater.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

// Events declared in test fakes below implement interface contracts but are never raised
// by the fake itself (the real implementation is tested separately).
#pragma warning disable CS0067

public class MainViewModelImportDialogTests
{
    // ── Fakes ──────────────────────────────────────────────────────────────

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private AppSettings _settings = new();

        public event EventHandler<AppSettings>? SettingsChanged;

        public AppSettings Load() => new AppSettings
        {
            Theme = _settings.Theme,
            Layout = _settings.Layout,
            PauseOnMaximized = _settings.PauseOnMaximized,
            MuteAudio = _settings.MuteAudio,
            GpuPreference = _settings.GpuPreference,
            LastRunVersion = _settings.LastRunVersion
        };

        public void Save(AppSettings settings)
        {
            _settings = settings;
            SettingsChanged?.Invoke(this, settings);
        }
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
            var entry = new WallpaperEntry
            {
                Id = Guid.NewGuid().ToString(),
                Title = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath),
                Video = sourceFilePath
            };
            Wallpapers.Add(entry);
            return Task.FromResult(entry);
        }

        public Task ShutdownAsync() => Task.CompletedTask;

        public Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> UpdateWallpaperMetadataAsync(string guid, string? title, string? author, CancellationToken cancellationToken = default) => Task.FromResult(false);
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

    private sealed class FakeUpdateService : IUpdateService
    {
        public Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(ReleaseChannel channel, CancellationToken cancellationToken = default)
            => Task.FromResult<(bool IsAvailable, UpdateManifest? Manifest)>((false, null));
    }

    // ── Test Fixture ────────────────────────────────────────────────────────

    private sealed class MainViewModelFixture : IDisposable
    {
        public FakeSettingsStore SettingsStore { get; } = new();
        public FakeGpuPreferenceService GpuService { get; } = new();
        public FakeWallpaperLibraryService LibraryService { get; } = new();
        public FakeLayoutPreferenceStore LayoutStore { get; } = new();
        public FakeUpdaterSettingsStore UpdaterSettingsStore { get; } = new();
        public NoOpDownloadManager DownloadManager { get; } = new();
        public AlwaysValidSignatureValidator SignatureValidator { get; } = new();
        public NoOpUpdateApplier UpdateApplier { get; } = new();
        public NoOpProcessManager ProcessManager { get; } = new();
        public FakeUpdateService UpdateService { get; } = new();

        public UpdateCoordinator Coordinator { get; }
        public TelemetryService TelemetryService { get; }
        public UpdaterViewModel UpdaterViewModel { get; }
        public LayoutHostViewModel LayoutHostViewModel { get; }
        public WallpaperService WallpaperService { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public DashboardViewModel DashboardViewModel { get; }
        public LibraryViewModel LibraryViewModel { get; }
        public PresentationManager PresentationManager { get; }
        public MainViewModel MainViewModel { get; }

        public MainViewModelFixture(AppSettings? initialSettings = null)
        {
            if (initialSettings != null)
                SettingsStore.Save(initialSettings);

            Coordinator = new UpdateCoordinator(
                UpdateService,
                DownloadManager,
                SignatureValidator,
                UpdateApplier,
                ProcessManager);

            TelemetryService = new TelemetryService();
            UpdaterViewModel = new UpdaterViewModel(Coordinator, UpdaterSettingsStore);
            LayoutHostViewModel = new LayoutHostViewModel(LayoutStore);
            WallpaperService = new WallpaperService(LibraryService, SettingsStore, GpuService);

            SettingsViewModel = new SettingsViewModel(
                WallpaperService,
                UpdaterViewModel,
                LayoutHostViewModel,
                SettingsStore);

            DashboardViewModel = new DashboardViewModel(
                WallpaperService,
                new DiagnosticsService(),
                SettingsStore);

            LibraryViewModel = new LibraryViewModel(WallpaperService);
            PresentationManager = new PresentationManager(WallpaperService, SettingsStore);

            MainViewModel = new MainViewModel(
                WallpaperService,
                TelemetryService,
                LibraryService,
                SettingsStore,
                UpdaterViewModel,
                DashboardViewModel,
                LibraryViewModel,
                SettingsViewModel,
                LayoutHostViewModel,
                PresentationManager);
        }

        public void Dispose()
        {
            MainViewModel?.Dispose();
            UpdaterViewModel?.Dispose();
            Coordinator?.Dispose();
        }
    }

    // ── Helper ─────────────────────────────────────────────────────────────

    private void InvokeCheckForVersionUpdate(MainViewModel vm)
    {
        var method = typeof(MainViewModel).GetMethod("CheckForVersionUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
            throw new Exception("Method CheckForVersionUpdate not found.");
        method.Invoke(vm, null);
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void CheckForVersionUpdate_MinimalLayout_ShowsPremiumGlassModalWithHighlights()
    {
        var settings = new AppSettings { Layout = "Minimal", LastRunVersion = "1.0.0" };
        using var fixture = new MainViewModelFixture(settings);

        InvokeCheckForVersionUpdate(fixture.MainViewModel);

        Assert.True(fixture.MainViewModel.IsWhatsNewVisible);
        Assert.False(fixture.MainViewModel.IsDialogVisible);
        Assert.NotNull(fixture.MainViewModel.WhatsNewHighlights);
        Assert.NotEmpty(fixture.MainViewModel.WhatsNewHighlights);
        Assert.Equal(fixture.UpdaterViewModel.CurrentVersion, fixture.MainViewModel.WhatsNewVersion);
    }

    [Fact]
    public void CheckForVersionUpdate_TechieLayout_ShowsStandardDialogWithMessage()
    {
        var settings = new AppSettings { Layout = "Techie", LastRunVersion = "1.0.0" };
        using var fixture = new MainViewModelFixture(settings);

        InvokeCheckForVersionUpdate(fixture.MainViewModel);

        Assert.False(fixture.MainViewModel.IsWhatsNewVisible);
        Assert.True(fixture.MainViewModel.IsDialogVisible);
        Assert.Contains("What's New in v", fixture.MainViewModel.DialogTitle);
        Assert.Contains("• ", fixture.MainViewModel.DialogMessage);
    }

    [Fact]
    public void CheckForVersionUpdate_SameHighlightsInBothLayouts()
    {
        // 1. Run Minimal layout
        var minimalSettings = new AppSettings { Layout = "Minimal", LastRunVersion = "1.0.0" };
        using var minimalFixture = new MainViewModelFixture(minimalSettings);
        InvokeCheckForVersionUpdate(minimalFixture.MainViewModel);
        var minimalHighlights = minimalFixture.MainViewModel.WhatsNewHighlights;

        // 2. Run Techie layout
        var techieSettings = new AppSettings { Layout = "Techie", LastRunVersion = "1.0.0" };
        using var techieFixture = new MainViewModelFixture(techieSettings);
        InvokeCheckForVersionUpdate(techieFixture.MainViewModel);
        var techieMessage = techieFixture.MainViewModel.DialogMessage;

        // 3. Verify that every highlight item in Minimal exists in the Techie multi-line string
        Assert.NotNull(minimalHighlights);
        Assert.NotEmpty(minimalHighlights);

        foreach (var highlight in minimalHighlights)
        {
            Assert.Contains(highlight, techieMessage);
        }
    }
}
