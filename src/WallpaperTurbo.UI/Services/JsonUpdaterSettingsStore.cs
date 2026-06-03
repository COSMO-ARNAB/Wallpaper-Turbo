using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.UI.Services;

public sealed class JsonUpdaterSettingsStore : IUpdaterSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;
    private readonly object _ioLock = new();

    public JsonUpdaterSettingsStore()
    {
        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperTurbo");
        Directory.CreateDirectory(appDataDir);
        _settingsPath = Path.Combine(appDataDir, "updater_settings.json");
    }

    public UpdaterSettings Load()
    {
        lock (_ioLock)
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new UpdaterSettings();
                }

                string json = File.ReadAllText(_settingsPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new UpdaterSettings();
                }

                var settings = JsonSerializer.Deserialize<UpdaterSettings>(json, SerializerOptions);
                return settings ?? new UpdaterSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonUpdaterSettingsStore] Failed to load settings: {ex.Message}. Falling back to defaults.");
                return new UpdaterSettings();
            }
        }
    }

    public void Save(UpdaterSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        lock (_ioLock)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, SerializerOptions);
                string tempPath = _settingsPath + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(_settingsPath))
                {
                    File.Replace(tempPath, _settingsPath, null);
                }
                else
                {
                    File.Move(tempPath, _settingsPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonUpdaterSettingsStore] Failed to save settings: {ex.Message}");
            }
        }
    }
}
