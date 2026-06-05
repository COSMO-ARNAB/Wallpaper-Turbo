using System.Text.Json.Serialization;

namespace WallpaperTurbo.Updater.Models;

public sealed class RemoteUpdateManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("generated_at")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("release_notes")]
    public string? ReleaseNotes { get; set; }

    [JsonPropertyName("installer_filename")]
    public string InstallerFilename { get; set; } = string.Empty;

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("min_supported_version")]
    public string? MinSupportedVersion { get; set; }

    [JsonPropertyName("min_signature_required")]
    public string? MinSignatureRequired { get; set; }

    [JsonPropertyName("rollback_eligible")]
    public bool? RollbackEligible { get; set; }
}
