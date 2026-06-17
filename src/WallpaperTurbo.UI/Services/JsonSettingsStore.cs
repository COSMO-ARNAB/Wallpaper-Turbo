using System;
using System.IO;
using System.Text.Json;
using WallpaperTurbo.UI.Models;

namespace WallpaperTurbo.UI.Services;

public class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _lock = new();
    private AppSettings? _cachedSettings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public JsonSettingsStore()
    {
        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperTurbo");
        _filePath = Path.Combine(appDataDir, "appsettings.json");
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (_cachedSettings != null)
            {
                return Clone(_cachedSettings);
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        _cachedSettings = settings;
                        return Clone(settings);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            var defaultSettings = new AppSettings();
            _cachedSettings = defaultSettings;
            return Clone(defaultSettings);
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            _cachedSettings = Clone(settings);
            try
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(settings, SerializerOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        // Fire event outside lock to avoid deadlocks
        SettingsChanged?.Invoke(this, Clone(settings));
    }

    private static AppSettings Clone(AppSettings source)
    {
        return new AppSettings
        {
            Theme = source.Theme,
            Layout = source.Layout,
            PauseOnMaximized = source.PauseOnMaximized,
            MuteAudio = source.MuteAudio,
            StartWithWindows = source.StartWithWindows,
            LastRunVersion = source.LastRunVersion,
            GpuPreference = source.GpuPreference
        };
    }
}
