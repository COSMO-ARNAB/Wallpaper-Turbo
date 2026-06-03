using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Updater.Services;

public sealed class GitHubReleaseProvider : IUpdateSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;

    public GitHubReleaseProvider(HttpClient httpClient, string owner, string repo)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task<UpdateManifest?> GetLatestManifestAsync(ReleaseChannel channel, CancellationToken cancellationToken = default)
    {
        string apiUrl = $"https://api.github.com/repos/{_owner}/{_repo}/releases";
        
        using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[GitHubReleaseProvider] Failed to fetch releases: {response.StatusCode}");
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        
        foreach (var releaseElement in doc.RootElement.EnumerateArray())
        {
            try
            {
                var manifest = ParseRelease(releaseElement, channel);
                if (manifest != null)
                {
                    return manifest;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GitHubReleaseProvider] Skipping invalid release: {ex.Message}");
                continue;
            }
        }

        return null;
    }

    private UpdateManifest? ParseRelease(JsonElement release, ReleaseChannel channel)
    {
        bool isPrerelease = release.TryGetProperty("prerelease", out var prereleaseElem) && prereleaseElem.GetBoolean();
        bool isDraft = release.TryGetProperty("draft", out var draftElem) && draftElem.GetBoolean();

        if (isDraft)
        {
            return null;
        }

        ReleaseChannel releaseChannel = isPrerelease ? ReleaseChannel.Nightly : ReleaseChannel.Stable;

        if (channel == ReleaseChannel.Stable && releaseChannel != ReleaseChannel.Stable)
        {
            return null; // Stable only accepts Stable
        }

        if (channel == ReleaseChannel.Preview && releaseChannel == ReleaseChannel.Nightly)
        {
            return null; // Preview accepts Preview and Stable, but not Nightly
        }

        // Nightly accepts Nightly, Preview, and Stable (all channels)

        var tagName = release.TryGetProperty("tag_name", out var tagNameElem)
            ? tagNameElem.GetString() 
            : null;
        
        if (string.IsNullOrEmpty(tagName) || !TryParseVersionFromTag(tagName, out var version))
        {
            return null;
        }

        var body = release.TryGetProperty("body", out var bodyElem)
            ? bodyElem.GetString() 
            : null;

        var assets = release.TryGetProperty("assets", out var assetsElem)
            ? assetsElem.EnumerateArray()
            : default;

        string downloadUrl = string.Empty;
        string sha256Hash = string.Empty;
        long fileSizeBytes = 0;

        foreach (var asset in assets)
        {
            var name = asset.TryGetProperty("name", out var nameElem)
                ? nameElem.GetString() 
                : null;
            
            if (!string.IsNullOrEmpty(name) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElem)
                    ? urlElem.GetString() ?? string.Empty
                    : string.Empty;
                fileSizeBytes = asset.TryGetProperty("size", out var sizeElem) ? sizeElem.GetInt64() : 0;
                
                var bodyText = body ?? string.Empty;
                var safeName = System.Text.RegularExpressions.Regex.Escape(name);
                var shaMatch = System.Text.RegularExpressions.Regex.Match(
                    bodyText, 
                    $@"([a-fA-F0-9]{{64}})\s+{safeName}",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (shaMatch.Success && shaMatch.Groups.Count > 1)
                {
                    sha256Hash = shaMatch.Groups[1].Value.ToLowerInvariant();
                }
                break;
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            return null;
        }

        return new UpdateManifest(
            Version: version,
            Channel: releaseChannel,
            ReleaseNotes: body ?? string.Empty,
            DownloadUrl: downloadUrl,
            Sha256Hash: sha256Hash,
            FileSizeBytes: fileSizeBytes,
            MinSupportedVersion: new Version(1, 0, 0),
            IsRollbackEligible: false
        );
    }

    private static bool TryParseVersionFromTag(string tagName, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrEmpty(tagName))
        {
            return false;
        }

        var cleanedTag = tagName.TrimStart('v', 'V');
        if (Version.TryParse(cleanedTag, out var parsedVersion))
        {
            version = parsedVersion;
            return true;
        }
        
        return TryFallbackParse(cleanedTag, out version);
    }

    private static bool TryFallbackParse(string input, out Version version)
    {
        version = new Version(0, 0, 0);
        var parts = input.Split('.');
        if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
        {
            int minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
            int build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
            version = new Version(major, minor, build);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        // _httpClient is injected and managed externally, do not dispose here
    }
}