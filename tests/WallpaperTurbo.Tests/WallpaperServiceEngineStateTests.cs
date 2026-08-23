using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

// Events declared in test fakes below implement interface contracts but are never raised
// by the fake itself (the real implementation is tested separately).
#pragma warning disable CS0067

public class WallpaperServiceEngineStateTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        private AppSettings _settings = new();

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
        public event EventHandler<WallpaperEntry>? MetadataChanged;
        public Task<IReadOnlyList<WallpaperEntry>> GetWallpapersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WallpaperEntry>>(new List<WallpaperEntry>());
        public Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default, IProgress<ImportProgress>? progress = null)
            => throw new NotImplementedException();
        public Task ShutdownAsync() => Task.CompletedTask;
        public Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<bool> UpdateWallpaperMetadataAsync(string guid, string? title, string? author, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private static WallpaperService CreateService(bool engineAlive = false)
    {
        var service = new WallpaperService(
            new FakeWallpaperLibraryService(),
            new FakeSettingsStore(),
            new FakeGpuPreferenceService());

        // A real AppRunner is usually running on a dev machine, which would otherwise decide the
        // outcome of these tests.
        service.AppRunnerProcessProbe = () => engineAlive;
        return service;
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{name}' not found.");
        field.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{name}' not found.");
        return (T)field.GetValue(target)!;
    }

    /// <summary>
    /// Regression: the 200ms process-probe cache used to short-circuit the whole method, skipping
    /// the state reconciliation that <c>IsEngineRunning</c> owns — the active-index reset, the
    /// state-file sync and the engine-died session publish. Callers such as
    /// <c>ReloadWallpapers</c> (which calls <c>IsEngineRunning()</c> and then immediately reads
    /// <c>_activeWallpaperIndex</c>) were left rendering a stale active highlight.
    /// </summary>
    [Fact]
    public void IsEngineRunning_ReconcilesActiveIndex_EvenOnAProcessProbeCacheHit()
    {
        var service = CreateService();
        int probes = 0;
        service.AppRunnerProcessProbe = () => { probes++; return false; };

        // First call performs the real probe and primes the 200ms cache.
        Assert.False(service.IsEngineRunning());
        Assert.Equal(1, probes);

        // Simulate state drifting out from under us (e.g. an engine crash observed elsewhere).
        SetPrivateField(service, "_activeWallpaperIndex", 3);

        // Second call lands inside the cache window: the expensive probe must be skipped...
        Assert.False(service.IsEngineRunning());
        Assert.Equal(1, probes);

        // ...but the reconciliation must still have run.
        Assert.Equal(-1, GetPrivateField<int>(service, "_activeWallpaperIndex"));
    }

    [Fact]
    public void IsEngineRunning_PublishesStoppedSession_OnceOnTransition()
    {
        var service = CreateService();
        int publishes = 0;
        service.SessionStateChanged += (_, e) =>
        {
            if (!e.IsActive)
            {
                publishes++;
            }
        };

        // First observation of "not running" is a transition and must publish.
        Assert.False(service.IsEngineRunning());
        Assert.Equal(1, publishes);

        // Repeat observations are not transitions and must stay quiet — this is what keeps the
        // power-management SessionStateChanged subscription from feeding back on itself.
        Assert.False(service.IsEngineRunning());
        Assert.False(service.IsEngineRunning());
        Assert.Equal(1, publishes);
    }

    [Fact]
    public void IsEngineRunning_ClearsStateFileWatermark_WhenEngineIsDown()
    {
        var service = CreateService();

        SetPrivateField(service, "_lastStateFileWriteTime", DateTime.UtcNow);

        Assert.False(service.IsEngineRunning());

        Assert.Equal(DateTime.MinValue, GetPrivateField<DateTime>(service, "_lastStateFileWriteTime"));
    }

    [Fact]
    public void IsEngineRunning_ReportsTrue_WhenTheProbeSeesAnAppRunner()
    {
        var service = CreateService(engineAlive: true);

        Assert.True(service.IsEngineRunning());
    }
}
