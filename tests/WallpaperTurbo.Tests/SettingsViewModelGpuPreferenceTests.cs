using System;
using System.Collections.Generic;
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

/// <summary>
/// Integration tests for SettingsViewModel GPU preference behavior.
/// Verifies that the fix for stale-apply (cancellation check after debounce) works correctly.
/// </summary>
public class SettingsViewModelGpuPreferenceTests
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
            GpuPreference = _settings.GpuPreference
        };

        public void Save(AppSettings settings)
        {
            _settings = settings;
            SettingsChanged?.Invoke(this, settings);
        }
    }

    private sealed class FakeGpuPreferenceService : IGpuPreferenceService
    {
        private readonly Dictionary<string, GpuPreference> _registry = new(StringComparer.OrdinalIgnoreCase);
        public List<(string ExePath, GpuPreference Mode)> SetCalls { get; } = new();

        public void SetGpuPreference(string exePath, GpuPreference mode)
        {
            SetCalls.Add((exePath, mode));
            if (mode == GpuPreference.Auto)
                _registry.Remove(exePath);
            else
                _registry[exePath] = mode;
        }

        public GpuPreference GetGpuPreference(string exePath)
        {
            return _registry.TryGetValue(exePath, out var mode) ? mode : GpuPreference.Auto;
        }
    }

    private sealed class FakeWallpaperLibraryService : IWallpaperLibraryService
    {
        public event EventHandler<WallpaperEntry>? MetadataChanged;

        public Task<IReadOnlyList<WallpaperEntry>> GetWallpapersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WallpaperEntry>>(Array.Empty<WallpaperEntry>());

        public Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default, IProgress<ImportProgress>? progress = null)
            => throw new NotImplementedException();

        public Task ShutdownAsync() => Task.CompletedTask;

        public Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> UpdateWallpaperMetadataAsync(string guid, string? title, string? author, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
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

    private sealed class SettingsFixture : IDisposable
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
        public UpdaterViewModel UpdaterViewModel { get; }
        public LayoutHostViewModel LayoutHostViewModel { get; }
        public WallpaperService WallpaperService { get; }
        public SettingsViewModel SettingsViewModel { get; }

        public SettingsFixture(AppSettings? initialSettings = null)
        {
            if (initialSettings != null)
                SettingsStore.Save(initialSettings);

            Coordinator = new UpdateCoordinator(
                UpdateService,
                DownloadManager,
                SignatureValidator,
                UpdateApplier,
                ProcessManager);

            UpdaterViewModel = new UpdaterViewModel(Coordinator, UpdaterSettingsStore);
            LayoutHostViewModel = new LayoutHostViewModel(LayoutStore);
            WallpaperService = new WallpaperService(LibraryService, SettingsStore, GpuService);

            SettingsViewModel = new SettingsViewModel(
                WallpaperService,
                UpdaterViewModel,
                LayoutHostViewModel,
                SettingsStore);
        }

        public void Dispose()
        {
            UpdaterViewModel?.Dispose();
            Coordinator?.Dispose();
        }
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void SettingsViewModel_Initializes_With_Correct_GpuPreference_From_Store()
    {
        var initialSettings = new AppSettings { GpuPreference = GpuPreference.Dedicated };
        using var fixture = new SettingsFixture(initialSettings);

        Assert.Equal(GpuPreference.Dedicated, fixture.SettingsViewModel.SelectedGpuPreference);
    }

    [Fact]
    public void SettingsViewModel_Initializes_With_Auto_When_No_Setting_Persisted()
    {
        using var fixture = new SettingsFixture();

        Assert.Equal(GpuPreference.Auto, fixture.SettingsViewModel.SelectedGpuPreference);
    }

    [Fact]
    public async Task SettingsViewModel_Rapid_GpuPreference_Changes_Only_Applies_Latest()
    {
        // This test verifies the fix for Bug #1:
        // When the user rapidly changes GPU preference (Auto → Integrated → Dedicated),
        // only the latest value (Dedicated) should be applied to the GPU service.
        // The stale Integrated apply should be cancelled.

        var initialSettings = new AppSettings { GpuPreference = GpuPreference.Auto };
        using var fixture = new SettingsFixture(initialSettings);

        // Verify initial state
        Assert.Equal(GpuPreference.Auto, fixture.SettingsViewModel.SelectedGpuPreference);
        Assert.Empty(fixture.GpuService.SetCalls);

        // Simulate rapid user changes: Auto → Integrated → Dedicated
        // The fix ensures only Dedicated is applied (Integrated is cancelled)

        // Step 1: User selects Integrated
        fixture.SettingsViewModel.SelectedGpuPreference = GpuPreference.Integrated;

        // Step 2: Immediately select Dedicated (before debounce completes)
        fixture.SettingsViewModel.SelectedGpuPreference = GpuPreference.Dedicated;

        // Wait for all async operations to settle (debounce is 600ms in production)
        await Task.Delay(800);

        // Assert: Only Dedicated should have been applied to the GPU service
        var appliedModes = fixture.GpuService.SetCalls.ConvertAll(c => c.Mode);
        Assert.DoesNotContain(GpuPreference.Integrated, appliedModes);
        Assert.Contains(GpuPreference.Dedicated, appliedModes);
    }

    [Fact]
    public async Task SettingsViewModel_GpuPreference_Change_Persists_To_SettingsStore()
    {
        using var fixture = new SettingsFixture();

        fixture.SettingsViewModel.SelectedGpuPreference = GpuPreference.Integrated;

        // Wait for debounce and apply
        await Task.Delay(800);

        var loaded = fixture.SettingsStore.Load();
        Assert.Equal(GpuPreference.Integrated, loaded.GpuPreference);
    }

    [Fact]
    public async Task SettingsViewModel_ResetAllSettings_Resets_GpuPreference_To_Auto()
    {
        var initialSettings = new AppSettings { GpuPreference = GpuPreference.Dedicated };
        using var fixture = new SettingsFixture(initialSettings);

        // Invoke the ResetAllSettings command (public via RelayCommand)
        await fixture.SettingsViewModel.ResetAllSettingsCommand.ExecuteAsync(null);

        // Wait for any pending GPU apply
        await Task.Delay(800);

        Assert.Equal(GpuPreference.Auto, fixture.SettingsViewModel.SelectedGpuPreference);
        var loaded = fixture.SettingsStore.Load();
        Assert.Equal(GpuPreference.Auto, loaded.GpuPreference);
    }

    [Fact]
    public async Task LaunchWallpaperAsync_Syncs_Registry_From_Persisted_Settings_Before_Launch()
    {
        // Regression test for Bug #3 (startup sync gap):
        // If the persisted setting is Dedicated but the registry is empty
        // (e.g., after a clean install, CCleaner, or driver update),
        // LaunchWallpaperAsync must write Dedicated to the registry
        // before spawning the engine process — otherwise the engine
        // launches on the wrong GPU.

        // Arrange: persisted = Dedicated, registry = empty
        var initialSettings = new AppSettings { GpuPreference = GpuPreference.Dedicated };

        // Create a fake WallpaperTurbo.AppRunner.exe in a temp directory.
        // We pass this path directly to the testable constructor overload so
        // WallpaperService doesn't probe the filesystem for a real AppRunner.
        var tempDir = Path.Combine(Path.GetTempPath(), "WallpaperTurbo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fakeExePath = Path.Combine(tempDir, "WallpaperTurbo.AppRunner.exe");
        await File.WriteAllBytesAsync(fakeExePath, new byte[] { 0x4D, 0x5A }); // MZ header

        try
        {
            using var fixture = new SettingsFixtureWithCustomAppRunner(initialSettings, fakeExePath);

            // Verify precondition: registry is empty (fake GPU service has no entry)
            Assert.Equal(GpuPreference.Auto, fixture.GpuService.GetGpuPreference(fakeExePath));

            // Act: launch wallpaper with forceFreshLaunch:true to bypass the IPC swap
            // path, making this test hermetic regardless of whether a real engine is running.
            // The GPU sync now always runs before the IPC check, so this also validates
            // the non-fresh-launch path if an engine is connected.
            await fixture.WallpaperService.LaunchWallpaperAsync(1, forceFreshLaunch: true);

            // Assert: registry now matches persisted setting
            Assert.Equal(GpuPreference.Dedicated, fixture.GpuService.GetGpuPreference(fakeExePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchWallpaperAsync_Does_Not_Sync_When_Registry_Already_Matches()
    {
        // If the registry already matches the persisted setting,
        // LaunchWallpaperAsync should NOT call SetGpuPreference again
        // (avoids unnecessary registry writes on every launch).
        var initialSettings = new AppSettings { GpuPreference = GpuPreference.Integrated };

        var tempDir = Path.Combine(Path.GetTempPath(), "WallpaperTurbo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fakeExePath = Path.Combine(tempDir, "WallpaperTurbo.AppRunner.exe");
        await File.WriteAllBytesAsync(fakeExePath, new byte[] { 0x4D, 0x5A });

        try
        {
            using var fixture = new SettingsFixtureWithCustomAppRunner(initialSettings, fakeExePath);
            fixture.GpuService.SetGpuPreference(fakeExePath, GpuPreference.Integrated);

            var setCallsBeforeLaunch = fixture.GpuService.SetCalls.Count;

            await fixture.WallpaperService.LaunchWallpaperAsync(1, forceFreshLaunch: true);

            // No new SetGpuPreference calls should have been made
            Assert.Equal(setCallsBeforeLaunch, fixture.GpuService.SetCalls.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Test Fixture with custom AppRunner path ──────────────────────────
    // Uses the testable WallpaperService constructor that accepts an explicit
    // exe path, avoiding the filesystem probe entirely and eliminating the
    // need for brittle reflection-based readonly field overrides.

    private sealed class SettingsFixtureWithCustomAppRunner : IDisposable
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
        public UpdaterViewModel UpdaterViewModel { get; }
        public LayoutHostViewModel LayoutHostViewModel { get; }
        public WallpaperService WallpaperService { get; }

        public SettingsFixtureWithCustomAppRunner(AppSettings? initialSettings, string fakeExePath)
        {
            if (initialSettings != null)
                SettingsStore.Save(initialSettings);

            Coordinator = new UpdateCoordinator(
                UpdateService,
                DownloadManager,
                SignatureValidator,
                UpdateApplier,
                ProcessManager);

            UpdaterViewModel = new UpdaterViewModel(Coordinator, UpdaterSettingsStore);
            LayoutHostViewModel = new LayoutHostViewModel(LayoutStore);

            // Use the testable constructor that accepts an explicit AppRunner path,
            // bypassing the filesystem probe and making tests fully deterministic.
            WallpaperService = new WallpaperService(LibraryService, SettingsStore, GpuService, fakeExePath);
        }

        public void Dispose()
        {
            UpdaterViewModel?.Dispose();
            Coordinator?.Dispose();
        }
    }
}