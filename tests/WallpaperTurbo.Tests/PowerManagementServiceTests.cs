using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.Services.Power;
using Xunit;

namespace WallpaperTurbo.Tests;

// Events declared in test fakes below implement interface contracts but are never raised
// by the fake itself (the real implementation is tested separately).
#pragma warning disable CS0067

public class PowerManagementServiceTests
{
    [Fact]
    public void PowerManagementService_ExposesPowerLineState()
    {
        // Assert PowerManagementService methods execute safely without throwing
        bool onBattery = PowerManagementService.IsOnBatteryPower();
        bool pluggedIn = PowerManagementService.IsPluggedIn();

        Assert.Equal(onBattery, !pluggedIn);
    }

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

    /// <summary>Records every decision request so tests can count evaluations.</summary>
    private sealed class RecordingPolicy : IBatterySaverPolicy
    {
        public List<PowerInputs> Calls { get; } = new();

        public PowerAction Decide(PowerInputs inputs)
        {
            Calls.Add(inputs);
            return PowerAction.None;
        }
    }

    private static WallpaperService CreateWallpaperService(ISettingsStore store)
    {
        var service = new WallpaperService(new FakeWallpaperLibraryService(), store, new FakeGpuPreferenceService());

        // A real AppRunner is usually alive on a dev machine. Left unpinned, IsEngineRunning would
        // sync from the live state file and publish an *active* session, which legitimately
        // schedules an extra evaluation and makes these call counts machine-dependent.
        service.AppRunnerProcessProbe = static () => false;
        return service;
    }

    private static void RaiseSessionStateChanged(WallpaperService service, WallpaperSessionEventArgs args)
    {
        var field = typeof(WallpaperService).GetField("SessionStateChanged", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("SessionStateChanged backing field not found.");
        var handler = (EventHandler<WallpaperSessionEventArgs>?)field.GetValue(service);
        handler?.Invoke(service, args);
    }

    [Fact]
    public void Constructor_EvaluatesImmediately_WithoutWaitingOnTheDebounceWindow()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();

        using var service = new PowerManagementService(CreateWallpaperService(store), store, policy, time);

        // Nothing has arrived yet to coalesce with, so startup must not be delayed.
        Assert.Single(policy.Calls);
    }

    /// <summary>
    /// Regression for the drop-style debounce: a burst of notifications must be <b>deferred</b> into
    /// one trailing evaluation, not silently discarded. Dropping them latched a stale
    /// PowerLineStatus reading that nothing later corrected.
    /// </summary>
    [Fact]
    public void SettingsNotifications_AreCoalescedIntoExactlyOneTrailingEvaluation()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();

        using var service = new PowerManagementService(CreateWallpaperService(store), store, policy, time);
        Assert.Single(policy.Calls);

        store.Save(new AppSettings());
        time.Advance(TimeSpan.FromMilliseconds(200));
        store.Save(new AppSettings());
        time.Advance(TimeSpan.FromMilliseconds(200));
        store.Save(new AppSettings());

        // 400ms of wall clock, but only 0ms since the last notification.
        Assert.Single(policy.Calls);

        time.Advance(PowerManagementService.EvaluationDebounceWindow);
        Assert.Equal(2, policy.Calls.Count);

        // And the window is not left armed.
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(2, policy.Calls.Count);
    }

    /// <summary>
    /// Regression for booting on battery: the startup evaluation runs before the engine exists, so
    /// it can never pause. A session becoming active must re-open the decision.
    /// </summary>
    [Fact]
    public void ActiveSession_TriggersAReEvaluation()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();
        var wallpaperService = CreateWallpaperService(store);

        using var service = new PowerManagementService(wallpaperService, store, policy, time);
        Assert.Single(policy.Calls);

        RaiseSessionStateChanged(wallpaperService, new WallpaperSessionEventArgs("id", "thumb", isPlaying: true, isActive: true));
        time.Advance(PowerManagementService.EvaluationDebounceWindow);

        Assert.Equal(2, policy.Calls.Count);
    }

    /// <summary>
    /// The engine-died publish comes out of <c>IsEngineRunning()</c>, which the evaluation itself
    /// calls. Ignoring inactive sessions is what stops that from feeding back on itself.
    /// </summary>
    [Fact]
    public void InactiveSession_DoesNotTriggerAReEvaluation()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();
        var wallpaperService = CreateWallpaperService(store);

        using var service = new PowerManagementService(wallpaperService, store, policy, time);
        Assert.Single(policy.Calls);

        RaiseSessionStateChanged(wallpaperService, new WallpaperSessionEventArgs("", "", isPlaying: false, isActive: false));
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Single(policy.Calls);
    }

    [Fact]
    public void Dispose_StopsPendingEvaluations()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();

        var service = new PowerManagementService(CreateWallpaperService(store), store, policy, time);
        Assert.Single(policy.Calls);

        store.Save(new AppSettings());
        service.Dispose();

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(policy.Calls);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSettingsChanged()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();

        var service = new PowerManagementService(CreateWallpaperService(store), store, policy, time);
        service.Dispose();

        store.Save(new AppSettings());
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Single(policy.Calls);
    }

    /// <summary>
    /// The service now has two public constructors. Microsoft.Extensions.DependencyInjection picks
    /// the greediest one it can satisfy, so this pins the registration shape used by
    /// <c>App.ConfigureServices</c>: without both TimeProvider and IBatterySaverPolicy registered,
    /// resolution would silently fall back to the parameterless-policy overload (or fail outright).
    /// </summary>
    [Fact]
    public void ResolvesFromDependencyInjection_UsingTheInjectableConstructor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore, FakeSettingsStore>();
        services.AddSingleton<IWallpaperLibraryService, FakeWallpaperLibraryService>();
        services.AddSingleton<IGpuPreferenceService, FakeGpuPreferenceService>();
        services.AddSingleton<WallpaperService>();

        // Mirrors App.ConfigureServices.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBatterySaverPolicy, BatterySaverPolicy>();
        services.AddSingleton<PowerManagementService>();

        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<PowerManagementService>();
        Assert.NotNull(service);
        service.Dispose();
    }
}
