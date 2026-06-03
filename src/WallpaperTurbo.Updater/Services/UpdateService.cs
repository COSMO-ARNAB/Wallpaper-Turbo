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
    private readonly IVersionComparer _versionComparer;
    private readonly Version _currentVersion;

    public UpdateService(IUpdateSourceProvider sourceProvider, IVersionComparer versionComparer)
    {
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
        _versionComparer = versionComparer ?? throw new ArgumentNullException(nameof(versionComparer));
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

        if (_versionComparer.IsUpdateAvailable(_currentVersion, manifest.Version))
        {
            Debug.WriteLine($"[UpdateService] Update available: {_currentVersion} -> {manifest.Version}");
            return (true, manifest);
        }

        Debug.WriteLine($"[UpdateService] No update available. Current: {_currentVersion}, Latest: {manifest.Version}");
        return (false, manifest);
    }

    private static Version GetCurrentVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version ?? new Version(1, 0, 0);
        }
        catch
        {
            return new Version(1, 0, 0);
        }
    }
}