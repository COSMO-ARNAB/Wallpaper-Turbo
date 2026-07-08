using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.UI;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

// Events declared in test fakes below implement interface contracts but are never raised
// by the fake itself (the real implementation is tested separately).
#pragma warning disable CS0067

public class PresentationManagerTests
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

    private void SimulateSessionStateChange(WallpaperService service, WallpaperSessionEventArgs args)
    {
        // Set private property ActiveSession
        var prop = typeof(WallpaperService).GetProperty("ActiveSession", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        prop?.SetValue(service, args);

        // Get private multicast delegate SessionStateChanged
        var field = typeof(WallpaperService).GetField("SessionStateChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var handler = field?.GetValue(service) as MulticastDelegate;
        if (handler != null)
        {
            foreach (var receiver in handler.GetInvocationList())
            {
                receiver.Method.Invoke(receiver.Target, new object[] { service, args });
            }
        }
    }

    [Fact]
    public void PresentationManager_Initializes_With_Solid_When_No_Active_Session()
    {
        var lib = new FakeWallpaperLibraryService();
        var store = new FakeSettingsStore();
        var gpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(lib, store, gpu);

        using var presentationManager = new PresentationManager(wallpaperService, store);

        Assert.False(presentationManager.IsWallpaperVisible);
        Assert.Equal(WindowBackdropMode.None, presentationManager.BackdropMode);
        Assert.Equal(UIMaterialMode.Solid, presentationManager.MaterialMode);
    }

    [Fact]
    public void PresentationManager_Transitions_To_Glass_When_Wallpaper_Is_Active_And_Playing()
    {
        var lib = new FakeWallpaperLibraryService();
        var store = new FakeSettingsStore();
        var gpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(lib, store, gpu);

        using var presentationManager = new PresentationManager(wallpaperService, store);

        // Track property changed events
        int propertyChangedCount = 0;
        presentationManager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PresentationManager.IsWallpaperVisible) ||
                e.PropertyName == nameof(PresentationManager.BackdropMode) ||
                e.PropertyName == nameof(PresentationManager.MaterialMode))
            {
                propertyChangedCount++;
            }
        };

        // Simulate active and playing session state change
        var activeSession = new WallpaperSessionEventArgs("Sunset Walk", "thumb.jpg", true, true);
        SimulateSessionStateChange(wallpaperService, activeSession);

        Assert.True(presentationManager.IsWallpaperVisible);
        Assert.Equal(WindowBackdropMode.Acrylic, presentationManager.BackdropMode);
        Assert.Equal(UIMaterialMode.Glass, presentationManager.MaterialMode);
        Assert.True(propertyChangedCount > 0);

        // Reset counter
        propertyChangedCount = 0;

        // Revert to non-active/non-playing session
        var inactiveSession = new WallpaperSessionEventArgs("", "", false, false);
        SimulateSessionStateChange(wallpaperService, inactiveSession);

        Assert.False(presentationManager.IsWallpaperVisible);
        Assert.Equal(WindowBackdropMode.None, presentationManager.BackdropMode);
        Assert.Equal(UIMaterialMode.Solid, presentationManager.MaterialMode);
        Assert.True(propertyChangedCount > 0);
    }

    [Fact]
    public void PresentationManager_Keeps_Glass_When_Wallpaper_Is_Active_But_Paused()
    {
        var lib = new FakeWallpaperLibraryService();
        var store = new FakeSettingsStore();
        var gpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(lib, store, gpu);

        using var presentationManager = new PresentationManager(wallpaperService, store);

        var pausedVisibleSession = new WallpaperSessionEventArgs("Sunset Walk", "thumb.jpg", false, true);
        SimulateSessionStateChange(wallpaperService, pausedVisibleSession);

        Assert.True(presentationManager.IsWallpaperVisible);
        Assert.Equal(WindowBackdropMode.Acrylic, presentationManager.BackdropMode);
        Assert.Equal(UIMaterialMode.Glass, presentationManager.MaterialMode);
    }

    [Theory]
    [InlineData("Acrylic", WindowBackdropMode.Acrylic)]
    [InlineData("Mica", WindowBackdropMode.Mica)]
    [InlineData("None", WindowBackdropMode.None)]
    [InlineData("Tabbed", WindowBackdropMode.Tabbed)]
    public void PresentationManager_Maps_Backdrop_Settings_To_Dwm_Constants(
        string setting,
        WindowBackdropMode expectedMode)
    {
        var lib = new FakeWallpaperLibraryService();
        var store = new FakeSettingsStore();
        store.Save(new AppSettings { GlassBackdrop = setting });
        var gpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(lib, store, gpu);

        using var presentationManager = new PresentationManager(wallpaperService, store);

        var activeSession = new WallpaperSessionEventArgs("Sunset Walk", "thumb.jpg", true, true);
        SimulateSessionStateChange(wallpaperService, activeSession);

        Assert.Equal(expectedMode, presentationManager.BackdropMode);
    }

    [Fact]
    public void WindowBackdropMode_Uses_Windows_Dwm_SystemBackdrop_Values()
    {
        Assert.Equal(0, (int)WindowBackdropMode.Auto);
        Assert.Equal(1, (int)WindowBackdropMode.None);
        Assert.Equal(2, (int)WindowBackdropMode.Mica);
        Assert.Equal(3, (int)WindowBackdropMode.Acrylic);
        Assert.Equal(4, (int)WindowBackdropMode.Tabbed);
    }

    [Fact]
    public void PresentationManager_DoesNotTriggerDuplicateTransitions_OnSameState()
    {
        var lib = new FakeWallpaperLibraryService();
        var store = new FakeSettingsStore();
        var gpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(lib, store, gpu);

        using var presentationManager = new PresentationManager(wallpaperService, store);

        // First transition to active
        var activeSession = new WallpaperSessionEventArgs("Sunset Walk", "thumb.jpg", true, true);
        SimulateSessionStateChange(wallpaperService, activeSession);

        int eventCount = 0;
        presentationManager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PresentationManager.IsWallpaperVisible))
            {
                eventCount++;
            }
        };

        // Simulate duplicate active event (e.g. telemetry poll)
        SimulateSessionStateChange(wallpaperService, activeSession);

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void PresentationManager_StressTest_Transitions()
    {
        var lib = new FakeWallpaperLibraryService();
        var store = new FakeSettingsStore();
        var gpu = new FakeGpuPreferenceService();
        var wallpaperService = new WallpaperService(lib, store, gpu);

        using var presentationManager = new PresentationManager(wallpaperService, store);

        var activeSession = new WallpaperSessionEventArgs("Sunset Walk", "thumb.jpg", true, true);
        var inactiveSession = new WallpaperSessionEventArgs("", "", false, false);

        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            SimulateSessionStateChange(wallpaperService, activeSession);
            Assert.True(presentationManager.IsWallpaperVisible);

            SimulateSessionStateChange(wallpaperService, inactiveSession);
            Assert.False(presentationManager.IsWallpaperVisible);
        }

        watch.Stop();

        // 100 full roundtrip transitions (200 state changes) should execute almost instantaneously (typically <10ms)
        Assert.True(watch.ElapsedMilliseconds < 100, $"100 transitions took too long: {watch.ElapsedMilliseconds}ms");
    }
}
