using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperTurbo.Core.Hardware;

namespace WallpaperTurbo.UI.Models;

public class AppSettings
{
    public const string CanonicalDefaultWallpaperId = "frieren-astral";

    private string? _lastActiveWallpaperId = CanonicalDefaultWallpaperId;
    private double _glassOpacity = 40.0;

    public string Theme { get; set; } = "Dark";
    public string Layout { get; set; } = "Minimal";
    public bool PauseOnMaximized { get; set; } = true;
    public bool MuteAudio { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;

    public string LastRunVersion { get; set; } = string.Empty;

    public string? LastActiveWallpaperId
    {
        get => _lastActiveWallpaperId;
        set => _lastActiveWallpaperId = value != null ? NormalizeWallpaperId(value) : null;
    }

    public bool AutoStartWallpaperEngine { get; set; } = true;

    public bool RememberLastWallpaper { get; set; } = true;

    public string GlassBackdrop { get; set; } = "Mica";
    public double GlassOpacity
    {
        get => _glassOpacity;
        set => _glassOpacity = NormalizeGlassOpacity(value);
    }

    public bool UseHardwareAcceleration { get; set; } = true;
    public bool BatterySaverEnabled { get; set; } = false;
    public bool LowPowerStaticPreview { get; set; } = false;

    [JsonConverter(typeof(GpuPreferenceJsonConverter))]
    public GpuPreference GpuPreference { get; set; } = GpuPreference.Auto;

    public static string NormalizeWallpaperId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.Equals(id, "crimson-blind", StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalDefaultWallpaperId;
        }
        return id;
    }

    public static double NormalizeGlassOpacity(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 40.0;
        }
        var scaled = (value > 0.0 && value <= 1.0) ? Math.Round(value * 100.0) : Math.Round(value);
        return Math.Clamp(scaled, 0.0, 90.0);
    }
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
