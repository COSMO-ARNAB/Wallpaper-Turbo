using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Updater.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly IUpdateSourceProvider _sourceProvider;
    private readonly SemanticVersion _currentVersion;

    public UpdateService(IUpdateSourceProvider sourceProvider)
    {
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
        _currentVersion = GetCurrentVersion();
    }

    public async Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(
        ReleaseChannel channel, 
        CancellationToken cancellationToken = default)
    {
        var manifest = await _sourceProvider.GetLatestManifestAsync(channel, cancellationToken);
        
        if (manifest == null)
        {
            return (false, null);
        }

        if (manifest.Version > _currentVersion)
        {
            Debug.WriteLine($"[UpdateService] Update available: {_currentVersion} -> {manifest.Version}");
            return (true, manifest);
        }

        Debug.WriteLine($"[UpdateService] No update available. Current: {_currentVersion}, Latest: {manifest.Version}");
        return (false, manifest);
    }

    private static SemanticVersion GetCurrentVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            
            var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (infoVersionAttr != null && SemanticVersion.TryParse(infoVersionAttr.InformationalVersion, out var semVer))
            {
                return semVer;
            }

            var version = assembly.GetName().Version;
            if (version != null)
            {
                return new SemanticVersion(version.Major, version.Minor, version.Build);
            }
            return new SemanticVersion(1, 0, 0);
        }
        catch
        {
            return new SemanticVersion(1, 0, 0);
        }
    }
}