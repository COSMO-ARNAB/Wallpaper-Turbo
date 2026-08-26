using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

// Events declared in the fakes below implement interface contracts but are never raised by the
// fake itself.
#pragma warning disable CS0067

/// <summary>
/// The battery-saver launch gate. Before this existed, startup launched the engine unconditionally
/// (it checked only AutoStartWallpaperEngine) and PowerManagementService stopped it ~500ms later
/// once the session went active — so on battery the user paid a full process spawn, GPU init and
/// decoded first frame, saw a flash of wallpaper, and then watched it vanish.
/// </summary>
public class WallpaperStartupCoordinatorBatterySaverTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        private AppSettings _settings;

        public FakeSettingsStore(AppSettings settings) => _settings = settings;

        public event EventHandler<AppSettings>? SettingsChanged;
        public AppSettings Load() => _settings;
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
        public List<WallpaperEntry> Wallpapers { get; } = new();

        public event EventHandler<WallpaperEntry>? MetadataChanged;
        public Task<IReadOnlyList<WallpaperEntry>> GetWallpapersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WallpaperEntry>>(Wallpapers);
        public Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default, IProgress<ImportProgress>? progress = null)
            => throw new NotImplementedException();
        public Task ShutdownAsync() => Task.CompletedTask;
        public Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<bool> UpdateWallpaperMetadataAsync(string guid, string? title, string? author, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class Fixture
    {
        public required FakeWallpaperLibraryService Library { get; init; }
        public required WallpaperService WallpaperService { get; init; }
        public required WallpaperStartupCoordinator Coordinator { get; init; }

        /// <summary>1-based indices the coordinator asked to launch. Empty means nothing started.</summary>
        public required List<int> LaunchedIndices { get; init; }
    }

    /// <summary>
    /// RememberLastWallpaper is off so resolution cannot reach the real LocalAppData history file —
    /// selection falls to the first library entry, which is index 1 to AppRunner.
    /// </summary>
    private static Fixture CreateFixture(bool batterySaverEnabled, bool onBattery, bool autoStart = true)
    {
        var settings = new AppSettings
        {
            AutoStartWallpaperEngine = autoStart,
            BatterySaverEnabled = batterySaverEnabled,
            RememberLastWallpaper = false
        };
        var store = new FakeSettingsStore(settings);

        var library = new FakeWallpaperLibraryService();
        library.Wallpapers.Add(new WallpaperEntry { Id = "wp-1", Title = "First", Video = "a.mp4" });
        library.Wallpapers.Add(new WallpaperEntry { Id = "wp-2", Title = "Second", Video = "b.mp4" });

        var wallpaperService = new WallpaperService(library, store, new FakeGpuPreferenceService());
        wallpaperService.AppRunnerProcessProbe = static () => false;

        // Belt and braces alongside the LaunchPlayback seam below: nothing here may reach the real
        // named pipe, which belongs to whatever engine is running on the developer's machine.
        wallpaperService.IpcCommandOverride = static _ => Task.FromResult("error");

        var launched = new List<int>();

        var coordinator = new WallpaperStartupCoordinator(library, wallpaperService, store)
        {
            OnBatteryProbe = () => onBattery,
            // No live AppRunner: keeps adoption (and the real state file / IPC ping) out of it, so
            // the result does not depend on whether the dev machine is running the app right now.
            AppRunnerPidProbe = static () => new HashSet<int>(),
            // Records the attempt instead of performing it. The real launcher sends an IPC
            // `swap` to any live engine *before* checking for the exe, so without this the
            // "must not suppress" cases below would change the developer's own wallpaper.
            // Returning false short-circuits the readiness poll, which would otherwise spin
            // for 10s against the empty PID probe.
            LaunchPlayback = index => { launched.Add(index); return Task.FromResult(false); }
        };

        return new Fixture
        {
            Library = library,
            WallpaperService = wallpaperService,
            Coordinator = coordinator,
            LaunchedIndices = launched
        };
    }

    [Fact]
    public async Task DeclinesToLaunch_OnBattery_WhenBatterySaverIsEnabled()
    {
        var fixture = CreateFixture(batterySaverEnabled: true, onBattery: true);

        var result = await fixture.Coordinator.EnsureWallpaperRunningAsync();

        Assert.True(result.SuppressedByBatterySaver);
        Assert.False(result.IsEngineRunning);
        Assert.False(result.TimedOut);

        // The point of the whole fix: no process spawn, so no flash of wallpaper.
        Assert.Empty(fixture.LaunchedIndices);

        // The wallpaper that *would* have played is still reported, so the UI can name what is
        // deferred rather than showing a bare "nothing running".
        Assert.NotNull(result.ActiveWallpaper);
        Assert.Equal("First", result.ActiveWallpaper!.Title);
    }

    /// <summary>
    /// Without this, plugging in has nothing to relaunch: LastActiveWallpaperIndex is written only
    /// by StopPlaybackAsync, which never runs when the launch was declined, so it would stay -1 and
    /// the resume branch would silently do nothing.
    /// </summary>
    [Fact]
    public async Task RecordsTheDeferredIndex_SoResumeHasATarget()
    {
        var fixture = CreateFixture(batterySaverEnabled: true, onBattery: true);
        Assert.Equal(-1, fixture.WallpaperService.LastActiveWallpaperIndex);

        await fixture.Coordinator.EnsureWallpaperRunningAsync();

        // 1-based for AppRunner, and nothing was actually started.
        Assert.Equal(1, fixture.WallpaperService.LastActiveWallpaperIndex);
        Assert.Equal(-1, fixture.WallpaperService.ActiveWallpaperIndex);
    }

    [Fact]
    public async Task DoesNotSuppress_WhenPluggedIn()
    {
        var fixture = CreateFixture(batterySaverEnabled: true, onBattery: false);

        var result = await fixture.Coordinator.EnsureWallpaperRunningAsync();

        Assert.False(result.SuppressedByBatterySaver);

        // Battery saver on but plugged in must not gate anything: the launch still happens.
        Assert.Equal(new[] { 1 }, fixture.LaunchedIndices);
    }

    [Fact]
    public async Task DoesNotSuppress_OnBattery_WhenBatterySaverIsDisabled()
    {
        var fixture = CreateFixture(batterySaverEnabled: false, onBattery: true);

        var result = await fixture.Coordinator.EnsureWallpaperRunningAsync();

        Assert.False(result.SuppressedByBatterySaver);
        Assert.Equal(new[] { 1 }, fixture.LaunchedIndices);
    }

    /// <summary>
    /// Auto-start off is the user's own choice, not our suppression — reporting it as
    /// battery-saver suppression would make plugging in start a wallpaper they never asked for.
    /// </summary>
    [Fact]
    public async Task AutoStartDisabled_IsNotReportedAsBatterySaverSuppression()
    {
        var fixture = CreateFixture(batterySaverEnabled: true, onBattery: true, autoStart: false);

        var result = await fixture.Coordinator.EnsureWallpaperRunningAsync();

        Assert.False(result.SuppressedByBatterySaver);
        Assert.False(result.IsEngineRunning);
        Assert.Empty(fixture.LaunchedIndices);
        Assert.Equal(-1, fixture.WallpaperService.LastActiveWallpaperIndex);
    }

    [Fact]
    public async Task EmptyLibrary_IsNotReportedAsBatterySaverSuppression()
    {
        var fixture = CreateFixture(batterySaverEnabled: true, onBattery: true);
        fixture.Library.Wallpapers.Clear();

        var result = await fixture.Coordinator.EnsureWallpaperRunningAsync();

        Assert.False(result.SuppressedByBatterySaver);
        Assert.False(result.IsEngineRunning);
        Assert.Empty(fixture.LaunchedIndices);
    }
}
