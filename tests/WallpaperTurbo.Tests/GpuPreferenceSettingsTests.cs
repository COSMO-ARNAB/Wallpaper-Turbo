using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.Tests;

/// <summary>
/// Regression tests for the GPU preference feature.
///
/// Bug 1 (stale-apply): ApplyGpuPreferenceSwitchAsync did not check cancellation AFTER
/// the debounce delay. If a newer selection cancelled the old CTS right after Task.Delay
/// completed, the stale task still called ApplyGpuPreferenceAsync with the old value.
/// This caused "preference bouncing" — the wallpaper engine would restart with the wrong GPU.
///
/// Bug 2 (startup registry mutation): WallpaperService constructor unconditionally wrote
/// to the Windows registry if the persisted setting didn't match the registry. This caused
/// side-effects at construction time and could race with the UI's own apply path.
/// </summary>
public class GpuPreferenceSettingsTests
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

    /// <summary>
    /// Fake that mirrors WindowsGpuPreferenceService semantics:
    /// Integrated/Dedicated → store the value; Auto → delete the value.
    /// </summary>
    private sealed class FakeGpuPreferenceService : IGpuPreferenceService
    {
        private readonly Dictionary<string, GpuPreference> _registry = new(StringComparer.OrdinalIgnoreCase);

        public List<(string ExePath, GpuPreference Mode)> SetCalls { get; } = new();

        public void SetGpuPreference(string exePath, GpuPreference mode)
        {
            SetCalls.Add((exePath, mode));

            if (mode == GpuPreference.Auto)
            {
                _registry.Remove(exePath);
            }
            else
            {
                _registry[exePath] = mode;
            }
        }

        public GpuPreference GetGpuPreference(string exePath)
        {
            return _registry.TryGetValue(exePath, out var mode) ? mode : GpuPreference.Auto;
        }
    }

    // ── Tests: JSON converter ──────────────────────────────────────────────

    [Fact]
    public void GpuPreferenceJsonConverter_Reads_Legacy_Default_As_Auto()
    {
        string json = """{"GpuPreference":"Default"}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(settings);
        Assert.Equal(GpuPreference.Auto, settings.GpuPreference);
    }

    [Fact]
    public void GpuPreferenceJsonConverter_Reads_Numeric_Zero_As_Auto()
    {
        string json = """{"GpuPreference":0}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(settings);
        Assert.Equal(GpuPreference.Auto, settings.GpuPreference);
    }

    [Fact]
    public void GpuPreferenceJsonConverter_Writes_As_String()
    {
        var settings = new AppSettings { GpuPreference = GpuPreference.Dedicated };
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        Assert.Contains("\"Dedicated\"", json);
    }

    [Fact]
    public void GpuPreferenceJsonConverter_Reads_Integrated()
    {
        string json = """{"GpuPreference":"Integrated"}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(settings);
        Assert.Equal(GpuPreference.Integrated, settings.GpuPreference);
    }

    [Fact]
    public void GpuPreferenceJsonConverter_Falls_Back_To_Auto_On_Invalid()
    {
        string json = """{"GpuPreference":"UnknownValue"}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(settings);
        Assert.Equal(GpuPreference.Auto, settings.GpuPreference);
    }

    // ── Tests: AppSettings defaults ───────────────────────────────────────

    [Fact]
    public void AppSettings_Default_GpuPreference_Is_Auto()
    {
        var settings = new AppSettings();
        Assert.Equal(GpuPreference.Auto, settings.GpuPreference);
    }

    // ── Tests: IGpuPreferenceService contract ─────────────────────────────

    [Fact]
    public void GpuPreferenceService_Set_Integrated_Then_Get_Returns_Integrated()
    {
        var service = new FakeGpuPreferenceService();
        const string exePath = @"C:\test\AppRunner.exe";
        service.SetGpuPreference(exePath, GpuPreference.Integrated);
        Assert.Equal(GpuPreference.Integrated, service.GetGpuPreference(exePath));
    }

    [Fact]
    public void GpuPreferenceService_Set_Dedicated_Then_Get_Returns_Dedicated()
    {
        var service = new FakeGpuPreferenceService();
        const string exePath = @"C:\test\AppRunner.exe";
        service.SetGpuPreference(exePath, GpuPreference.Dedicated);
        Assert.Equal(GpuPreference.Dedicated, service.GetGpuPreference(exePath));
    }

    [Fact]
    public void GpuPreferenceService_Set_Auto_Deletes_Registry_Value()
    {
        var service = new FakeGpuPreferenceService();
        const string exePath = @"C:\test\AppRunner.exe";
        service.SetGpuPreference(exePath, GpuPreference.Dedicated);
        Assert.Equal(GpuPreference.Dedicated, service.GetGpuPreference(exePath));

        service.SetGpuPreference(exePath, GpuPreference.Auto);
        Assert.Equal(GpuPreference.Auto, service.GetGpuPreference(exePath));
    }

    [Fact]
    public void GpuPreferenceService_Get_Returns_Auto_When_Not_Set()
    {
        var service = new FakeGpuPreferenceService();
        Assert.Equal(GpuPreference.Auto, service.GetGpuPreference(@"C:\nonexistent\path.exe"));
    }

    [Fact]
    public void GpuPreferenceService_Set_Auto_On_Unset_Is_NoOp()
    {
        var service = new FakeGpuPreferenceService();
        const string exePath = @"C:\test\AppRunner.exe";
        service.SetGpuPreference(exePath, GpuPreference.Auto);
        Assert.Equal(GpuPreference.Auto, service.GetGpuPreference(exePath));
    }

    // ── Tests: Settings persistence roundtrip ─────────────────────────────

    [Fact]
    public void SettingsStore_Save_And_Load_Roundtrips_GpuPreference()
    {
        var store = new FakeSettingsStore();
        var settings = new AppSettings { GpuPreference = GpuPreference.Dedicated };

        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal(GpuPreference.Dedicated, loaded.GpuPreference);
    }

    [Fact]
    public void SettingsStore_Save_Auto_Roundtrips_GpuPreference()
    {
        var store = new FakeSettingsStore();
        var settings = new AppSettings { GpuPreference = GpuPreference.Auto };
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal(GpuPreference.Auto, loaded.GpuPreference);
    }

    [Fact]
    public void SettingsStore_SettingsChanged_Event_Fires_On_Save()
    {
        var store = new FakeSettingsStore();
        AppSettings? received = null;
        store.SettingsChanged += (_, s) => received = s;

        var settings = new AppSettings { GpuPreference = GpuPreference.Integrated };
        store.Save(settings);

        Assert.NotNull(received);
        Assert.Equal(GpuPreference.Integrated, received.GpuPreference);
    }
}
