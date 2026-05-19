// WallpaperLibrary.cs - Provides functionality to load wallpaper manifests in Wallpaper Turbo.
using System;
using System.Text.Json;
using WallpaperTurbo.Core.Models;

namespace WallpaperTurbo.Core.Media;

public static class WallpaperLibrary
{
    public static WallpaperManifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"Wallpaper manifest not found: {manifestPath}"
            );
        }

        string json = File.ReadAllText(manifestPath);

        var manifest = JsonSerializer.Deserialize<WallpaperManifest>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );

        if (manifest == null)
        {
            throw new InvalidOperationException(
                "Failed to deserialize wallpaper manifest."
            );
        }

        return manifest;
    }
}