using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
/// Adopting an engine the UI did not launch. The engine learns which PID owns the UI only from the
/// <c>--ui-pid</c> argument on its own command line, and that is passed exactly once, by the
/// <see cref="WallpaperService"/> launch path. So an engine that outlives a UI restart keeps
/// excluding the *dead* UI's process id: the new UI's windows are no longer recognised as its own,
/// and maximizing the app pauses the wallpaper it is supposed to be showing.
/// </summary>
public class WallpaperStartupCoordinatorAdoptionTests
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

    /// <summary>Stands in for a live AppRunner, recording what the UI asked it to do.</summary>
    private sealed class FakeEngineIpc
    {
        public List<string> Commands { get; } = new();

        public Task<string> Handle(string command)
        {
            Commands.Add(command);
            return Task.FromResult(command == "ping" ? "pong" : "success");
        }
    }

    /// <summary>
    /// Writes a state file that names <paramref name="enginePid"/> as the live engine, stamped now
    /// so it passes the coordinator's 5-minute freshness check.
    /// </summary>
    private static string WriteStateFile(string dir, int enginePid, int wallpaperIndex)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active_state.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            ProcessId = enginePid,
            ActiveWallpaperIndex = wallpaperIndex,
            ActiveWallpaperTitle = "First",
            IsPlaying = true,
            IpcPipeName = $"WallpaperTurbo_IPC_{enginePid}",
            UpdatedAtUtc = DateTime.UtcNow
        }));
        return path;
    }

    /// <summary>
    /// The regression: the engine is alive and gets adopted, so nothing ever passes it a fresh
    /// <c>--ui-pid</c>. Unless adoption tells it over IPC, its foreground watcher keeps excluding a
    /// process id that no longer exists.
    /// </summary>
    [Fact]
    public async Task AdoptingARunningEngine_TellsItTheCurrentUiProcessId()
    {
        const int enginePid = 424242;
        var dir = Path.Combine(Path.GetTempPath(), "wt-adopt-" + Guid.NewGuid().ToString("N"));
        var statePath = WriteStateFile(dir, enginePid, wallpaperIndex: 1);

        try
        {
            var library = new FakeWallpaperLibraryService();
            library.Wallpapers.Add(new WallpaperEntry { Id = "wp-1", Title = "First", Video = "a.mp4" });

            var store = new FakeSettingsStore(new AppSettings { RememberLastWallpaper = false });
            var ipc = new FakeEngineIpc();

            var wallpaperService = new WallpaperService(library, store, new FakeGpuPreferenceService());
            wallpaperService.IpcCommandOverride = ipc.Handle;

            var coordinator = new WallpaperStartupCoordinator(library, wallpaperService, store)
            {
                StateFilePathProvider = () => statePath,
                AppRunnerPidProbe = () => new HashSet<int> { enginePid },
                // Adoption must not need this; a launch would mean adoption failed.
                LaunchPlayback = _ => throw new InvalidOperationException("must adopt, not launch")
            };

            var result = await coordinator.EnsureWallpaperRunningAsync();

            Assert.True(result.IsEngineRunning);
            Assert.Equal("First", result.ActiveWallpaper!.Title);

            // The whole point: the adopted engine is told who the UI is now.
            Assert.Contains($"ui-pid {Environment.ProcessId}", ipc.Commands);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The announcement is worthless if it arrives before the engine has proved it can answer:
    /// the ping is what establishes there is a pipe to talk to at all.
    /// </summary>
    [Fact]
    public async Task TheProcessIdIsAnnouncedAfterTheLivenessPing()
    {
        const int enginePid = 424243;
        var dir = Path.Combine(Path.GetTempPath(), "wt-adopt-" + Guid.NewGuid().ToString("N"));
        var statePath = WriteStateFile(dir, enginePid, wallpaperIndex: 1);

        try
        {
            var library = new FakeWallpaperLibraryService();
            library.Wallpapers.Add(new WallpaperEntry { Id = "wp-1", Title = "First", Video = "a.mp4" });

            var store = new FakeSettingsStore(new AppSettings { RememberLastWallpaper = false, PauseOnMaximized = true, MuteAudio = true });
            var ipc = new FakeEngineIpc();

            var wallpaperService = new WallpaperService(library, store, new FakeGpuPreferenceService());
            wallpaperService.IpcCommandOverride = ipc.Handle;

            var coordinator = new WallpaperStartupCoordinator(library, wallpaperService, store)
            {
                StateFilePathProvider = () => statePath,
                AppRunnerPidProbe = () => new HashSet<int> { enginePid },
                LaunchPlayback = _ => throw new InvalidOperationException("must adopt, not launch")
            };

            await coordinator.EnsureWallpaperRunningAsync();

            Assert.Equal(new[] { "ping", $"ui-pid {Environment.ProcessId}", "pause-mode Maximized", "mute true" }, ipc.Commands);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AdoptingARunningEngine_SyncsDisabledPauseModeAndUnmutedAudio()
    {
        const int enginePid = 424244;
        var dir = Path.Combine(Path.GetTempPath(), "wt-adopt-" + Guid.NewGuid().ToString("N"));
        var statePath = WriteStateFile(dir, enginePid, wallpaperIndex: 1);

        try
        {
            var library = new FakeWallpaperLibraryService();
            library.Wallpapers.Add(new WallpaperEntry { Id = "wp-1", Title = "First", Video = "a.mp4" });

            var store = new FakeSettingsStore(new AppSettings { RememberLastWallpaper = false, PauseOnMaximized = false, MuteAudio = false });
            var ipc = new FakeEngineIpc();

            var wallpaperService = new WallpaperService(library, store, new FakeGpuPreferenceService());
            wallpaperService.IpcCommandOverride = ipc.Handle;

            var coordinator = new WallpaperStartupCoordinator(library, wallpaperService, store)
            {
                StateFilePathProvider = () => statePath,
                AppRunnerPidProbe = () => new HashSet<int> { enginePid },
                LaunchPlayback = _ => throw new InvalidOperationException("must adopt, not launch")
            };

            await coordinator.EnsureWallpaperRunningAsync();

            Assert.Contains("pause-mode None", ipc.Commands);
            Assert.Contains("mute false", ipc.Commands);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
