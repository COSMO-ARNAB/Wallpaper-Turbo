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

        /// <summary>The action <see cref="Decide"/> returns; default None keeps evaluations inert.</summary>
        public PowerAction Next { get; set; } = PowerAction.None;

        public PowerAction Decide(PowerInputs inputs)
        {
            Calls.Add(inputs);
            return Next;
        }

        public bool SuppressesPlayback(PowerInputs inputs)
            => inputs.BatterySaverEnabled && inputs.OnBattery;
    }

    /// <summary>
    /// Stands in for a live AppRunner on the other end of the named pipe, recording what the power
    /// service actually asked it to do.
    /// </summary>
    private sealed class FakeEngineIpc
    {
        /// <summary>Commands received, in order.</summary>
        public List<string> Commands { get; } = new();

        /// <summary>What the engine answers. Anything but "success" reads as unanswered.</summary>
        public string Reply { get; set; } = "success";

        public Task<string> Handle(string command)
        {
            Commands.Add(command);
            return Task.FromResult(Reply);
        }
    }

    private static WallpaperService CreateWallpaperService(
        ISettingsStore store,
        FakeEngineIpc? ipc = null,
        bool engineAlive = false)
    {
        var service = new WallpaperService(new FakeWallpaperLibraryService(), store, new FakeGpuPreferenceService());

        // A real AppRunner is usually alive on a dev machine. Left unpinned, IsEngineRunning would
        // sync from the live state file and publish an *active* session, which legitimately
        // schedules an extra evaluation and makes these call counts machine-dependent.
        service.AppRunnerProcessProbe = () => engineAlive;

        // Left unpinned, pause/play/swap would travel down the real named pipe and change the
        // wallpaper the developer is actually running. Tests that pass no recorder get an engine
        // that refuses everything, which keeps them inert.
        var engine = ipc ?? new FakeEngineIpc { Reply = "error" };
        service.IpcCommandOverride = engine.Handle;
        return service;
    }

    /// <summary>
    /// Freeze and thaw are started fire-and-forget, so there is no task to await. The fake engine
    /// answers synchronously, so this is normally satisfied on the first check.
    /// </summary>
    private static void WaitForCommands(FakeEngineIpc ipc, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (ipc.Commands.Count < count && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }
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

    /// <summary>
    /// Regression for battery saver acting as a silent off-switch: when startup declines to launch,
    /// nothing is ever paused, so without this notification the policy would see
    /// SuppressedByBatterySaver=false and return None on plug-in — leaving the desktop static for
    /// the rest of the session.
    /// </summary>
    [Fact]
    public void NotifyPlaybackSuppressedAtStartup_IsVisibleToTheNextDecision()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();

        using var service = new PowerManagementService(CreateWallpaperService(store), store, policy, time);
        Assert.False(policy.Calls[0].SuppressedByBatterySaver);

        service.NotifyPlaybackSuppressedAtStartup();
        service.EvaluatePowerState("test: after startup suppression");

        Assert.True(policy.Calls[^1].SuppressedByBatterySaver);
    }

    /// <summary>
    /// Resuming must clear ownership, otherwise every later evaluation would keep resuming.
    /// LastActiveWallpaperIndex is -1 here, so no process is actually launched.
    /// </summary>
    [Fact]
    public void Resuming_ClearsSuppressionOwnership()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();

        using var service = new PowerManagementService(CreateWallpaperService(store), store, policy, time);
        service.NotifyPlaybackSuppressedAtStartup();

        policy.Next = PowerAction.Resume;
        service.EvaluatePowerState("test: plugged in");
        Assert.True(policy.Calls[^1].SuppressedByBatterySaver);

        policy.Next = PowerAction.None;
        service.EvaluatePowerState("test: steady state");
        Assert.False(policy.Calls[^1].SuppressedByBatterySaver);
    }

    /// <summary>
    /// Regression for unplugging wiping the wallpaper: battery saver used to call
    /// StopPlaybackAsync, which spawns <c>AppRunner --stop</c> and kills the engine. The desktop
    /// went black — users read that as a crash, not a pause — and plugging back in cost a full cold
    /// launch. It must send the IPC pause instead, which freezes decoding and leaves the last frame
    /// up.
    /// </summary>
    [Fact]
    public void Pausing_FreezesTheEngineOverIpc_InsteadOfKillingTheProcess()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();
        var ipc = new FakeEngineIpc();

        using var service = new PowerManagementService(
            CreateWallpaperService(store, ipc, engineAlive: true), store, policy, time);

        policy.Next = PowerAction.Pause;
        service.EvaluatePowerState("test: on battery");
        WaitForCommands(ipc, 1);

        // Exactly one command, and it is a freeze. A stop would have shown up as no command at all
        // (it spawns a process), and a relaunch would have shown up as a "swap".
        Assert.Equal(new[] { "pause" }, ipc.Commands);
    }

    /// <summary>
    /// The engine survives a freeze, so plugging back in must unfreeze it rather than start a second
    /// one. Relaunching would spawn a duplicate process on top of the one already holding the
    /// desktop window.
    /// </summary>
    [Fact]
    public void Resuming_UnfreezesTheRunningEngine_RatherThanLaunchingAnother()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();
        var ipc = new FakeEngineIpc();

        using var service = new PowerManagementService(
            CreateWallpaperService(store, ipc, engineAlive: true), store, policy, time);

        policy.Next = PowerAction.Resume;
        service.EvaluatePowerState("test: plugged back in");
        WaitForCommands(ipc, 1);

        Assert.Equal(new[] { "play" }, ipc.Commands);
    }

    /// <summary>
    /// The other half: when nothing is running there is nothing to unfreeze. That is the startup
    /// case — booting on battery declines to launch at all — so resume has to fall back to launching
    /// the index the coordinator recorded.
    /// </summary>
    [Fact]
    public void Resuming_LaunchesTheRecordedWallpaper_WhenNoEngineIsRunning()
    {
        var store = new FakeSettingsStore();
        var policy = new RecordingPolicy();
        var time = new FakeTimeProvider();
        var ipc = new FakeEngineIpc();
        var wallpaperService = CreateWallpaperService(store, ipc, engineAlive: false);

        using var service = new PowerManagementService(wallpaperService, store, policy, time);
        wallpaperService.SetDeferredWallpaperIndex(2);

        policy.Next = PowerAction.Resume;
        service.EvaluatePowerState("test: plugged back in");
        WaitForCommands(ipc, 1);

        // No "play" first: asking a dead engine to unfreeze would waste a pipe timeout on every
        // plug-in. The launch is visible here as its live-swap attempt with pause mode sync, which the fake accepts.
        Assert.Equal(new[] { "swap 2", "pause-mode Maximized" }, ipc.Commands);
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
