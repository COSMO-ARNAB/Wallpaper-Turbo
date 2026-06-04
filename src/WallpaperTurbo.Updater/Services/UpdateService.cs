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
        UpdaterDiagnostic.Log("UpdateService.ctor", $"Current version resolved: {_currentVersion}");
    }

    public async Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(
        ReleaseChannel channel,
        CancellationToken cancellationToken = default)
    {
        UpdaterDiagnostic.Log("UpdateService.CheckForUpdatesAsync", $"Requested channel: {channel} | Current version: {_currentVersion}");
        Debug.WriteLine($"[UpdateService] Checking for updates on channel {channel}. Current version: {_currentVersion}");
        var manifest = await _sourceProvider.GetLatestManifestAsync(channel, cancellationToken);

        if (manifest == null)
        {
            UpdaterDiagnostic.Log("UpdateService.CheckForUpdatesAsync", $"REJECTION: No manifest returned by provider for channel {channel}.");
            Debug.WriteLine($"[UpdateService] No manifest returned from provider for channel {channel}.");
            return (false, null);
        }

        UpdaterDiagnostic.Log("UpdateService.CheckForUpdatesAsync", $"Manifest received: version={manifest.Version} channel={manifest.Channel} url={manifest.DownloadUrl} sha256={(string.IsNullOrEmpty(manifest.Sha256Hash) ? "<missing>" : manifest.Sha256Hash)} size={manifest.FileSizeBytes}");
        Debug.WriteLine($"[UpdateService] Received manifest for {manifest.Version}. Comparing to current {_currentVersion}.");

        if (manifest.Version > _currentVersion)
        {
            UpdaterDiagnostic.Log("UpdateService.CheckForUpdatesAsync", $"RESULT: IsAvailable=True | {manifest.Version} > {_currentVersion}");
            Debug.WriteLine($"[UpdateService] Update available: {_currentVersion} -> {manifest.Version}");
            return (true, manifest);
        }

        UpdaterDiagnostic.Log("UpdateService.CheckForUpdatesAsync", $"REJECTION: Manifest version {manifest.Version} is NOT greater than current {_currentVersion} (CompareTo={manifest.Version.CompareTo(_currentVersion)}). Update NOT available.");
        Debug.WriteLine($"[UpdateService] No update available. Current: {_currentVersion}, Latest: {manifest.Version} (Comparison: {manifest.Version > _currentVersion})");
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