using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperTurbo.Core.Services.Performance;

namespace WallpaperTurbo.Core.Models;

/// <summary>
/// Manages persistent renderer, decoder, and suspension configuration for Wallpaper Turbo.
/// </summary>
public sealed class WallpaperConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VideoDecodeMode DecodeMode { get; set; } = VideoDecodeMode.Auto;

    public string SuspendMode { get; set; } = "pause"; // "pause" or "stop"

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PauseMode PauseMode { get; set; } = PauseMode.Maximized;

    public bool DetachOnClose { get; set; } = true;

    public int FileCachingMs { get; set; } = 1000;

    public string? VideoOutputModule { get; set; } = null;

    public bool MemoryDiagnostics { get; set; } = false;

    public int DefaultWallpaperIndex { get; set; } = 1;

    public static WallpaperConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                return JsonSerializer.Deserialize<WallpaperConfig>(json, options) ?? new WallpaperConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Warning: Failed to load config from {path}: {ex.Message}");
        }

        var defaultConfig = new WallpaperConfig();
        defaultConfig.Save(path);
        return defaultConfig;
    }

    public void Save(string path)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            string json = JsonSerializer.Serialize(this, options);

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Warning: Failed to save config to {path}: {ex.Message}");
        }
    }
}
