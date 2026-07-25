using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperTurbo.Core.Hardware;

namespace WallpaperTurbo.UI.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Layout { get; set; } = "Minimal";
    public bool PauseOnMaximized { get; set; } = true;
    public bool MuteAudio { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;

    public string LastRunVersion { get; set; } = string.Empty;

    public string? LastActiveWallpaperId { get; set; }

    public bool AutoStartWallpaperEngine { get; set; } = true;

    public bool RememberLastWallpaper { get; set; } = true;

    public string GlassBackdrop { get; set; } = "Glass";
    public double GlassOpacity { get; set; } = 0.40;

    public bool UseHardwareAcceleration { get; set; } = true;

    [JsonConverter(typeof(GpuPreferenceJsonConverter))]
    public GpuPreference GpuPreference { get; set; } = GpuPreference.Auto;
}

/// <summary>
/// Custom JSON converter for GpuPreference to safely handle legacy/upgrade migrations (e.g. mapping "Default" to GpuPreference.Auto).
/// </summary>
public class GpuPreferenceJsonConverter : JsonConverter<GpuPreference>
{
    public override GpuPreference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? value = reader.GetString();
            if (string.IsNullOrEmpty(value)) return GpuPreference.Auto;
            if (string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
                return GpuPreference.Auto;
            if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
                return GpuPreference.Auto;
            if (Enum.TryParse<GpuPreference>(value, true, out var result))
                return result;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            int val = reader.GetInt32();
            if (Enum.IsDefined(typeof(GpuPreference), val))
                return (GpuPreference)val;
        }
        return GpuPreference.Auto;
    }

    public override void Write(Utf8JsonWriter writer, GpuPreference value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
