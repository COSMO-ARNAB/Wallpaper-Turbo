using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.Tests;

// Event in the test fake below implements the interface contract but is never raised.
#pragma warning disable CS0067

public class WallpaperServiceArgsTests
{
    [Fact]
    public void BuildAppRunnerArguments_IncludesPipeName()
    {
        string args = WallpaperService.BuildAppRunnerArguments(
            index: 2, pauseMode: "Maximized", softDecode: false, isMuted: true, uiPid: 123,
            pipeName: "WallpaperTurbo_IPC_123_abc");

        Assert.Contains("--wallpaper 2", args);
        Assert.Contains("--pipe-name WallpaperTurbo_IPC_123_abc", args);
        Assert.Contains("--ui-pid 123", args);
    }

    [Fact]
    public void BuildAppRunnerArguments_WithoutPipeName_OmitsFlag()
    {
        string args = WallpaperService.BuildAppRunnerArguments(
            index: 1, pauseMode: "None", softDecode: true, isMuted: false, uiPid: 456,
            pipeName: null);

        Assert.DoesNotContain("--pipe-name", args);
        Assert.Contains("--software-decode", args);
    }

    [Fact]
    public void BuildAppRunnerArguments_BlankPipeName_OmitsFlag()
    {
        string args = WallpaperService.BuildAppRunnerArguments(
            index: 1, pauseMode: "Maximized", softDecode: false, isMuted: true, uiPid: 1,
            pipeName: "   ");

        Assert.DoesNotContain("--pipe-name", args);
    }

    [Fact]
    public void CreateLaunchPipeName_IsUniqueAndSafe()
    {
        string first = WallpaperService.CreateLaunchPipeName();
        string second = WallpaperService.CreateLaunchPipeName();

        Assert.NotEqual(first, second);
        Assert.StartsWith("WallpaperTurbo_IPC_", first);
        Assert.All(first, c => Assert.True(
            char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.',
            $"Unsafe pipe-name character: {c}"));
    }

    [Fact]
    public void SetExplicitPipeName_PinsAndClearsRendezvousPipe()
    {
        var service = new WallpaperService(
            new FakeLibraryService(), new FakeSettingsStore(), new FakeGpuPreferenceService());

        service.SetExplicitPipeName("WallpaperTurbo_IPC_9_test");
        Assert.Equal("WallpaperTurbo_IPC_9_test", GetField<string?>(service, "_explicitIpcPipeName"));

        // Clearing must also drop the cached state-file pipe so the next read re-resolves
        // (adopt path) instead of redialing a dead pipe.
        SetField(service, "_cachedIpcPipeName", "WallpaperTurbo_IPC_stale");
        service.SetExplicitPipeName(null);
        Assert.Null(GetField<string?>(service, "_explicitIpcPipeName"));
        Assert.Null(GetField<string?>(service, "_cachedIpcPipeName"));
    }

    [Fact]
    public async Task StopPlaybackAsync_ClearsExplicitPipeName()
    {
        var service = new WallpaperService(
            new FakeLibraryService(), new FakeSettingsStore(), new FakeGpuPreferenceService());

        service.SetExplicitPipeName("WallpaperTurbo_IPC_9_test");
        Assert.NotNull(GetField<string?>(service, "_explicitIpcPipeName"));

        await service.StopPlaybackAsync();
        Assert.Null(GetField<string?>(service, "_explicitIpcPipeName"));
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{name}' not found.");
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{name}' not found.");
        return (T)field.GetValue(target)!;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private AppSettings _settings = new();
        public event EventHandler<AppSettings>? SettingsChanged;
        public AppSettings Load() => _settings;
        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class FakeGpuPreferenceService : IGpuPreferenceService
    {
        public void SetGpuPreference(string exePath, GpuPreference mode) { }
        public GpuPreference GetGpuPreference(string exePath) => GpuPreference.Auto;
    }

    private sealed class FakeLibraryService : IWallpaperLibraryService
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
}
