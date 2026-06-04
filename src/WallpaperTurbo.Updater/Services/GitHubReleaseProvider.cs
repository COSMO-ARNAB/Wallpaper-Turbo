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
        UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"GET {apiUrl} | requested channel={channel}");

        using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
        UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"HTTP status: {(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"REJECTION: Non-success HTTP status {(int)response.StatusCode} {response.StatusCode} from {apiUrl}");
            Debug.WriteLine($"[GitHubReleaseProvider] Failed to fetch releases: {response.StatusCode}");
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        int releaseCount = 0;
        foreach (var _ in doc.RootElement.EnumerateArray()) releaseCount++;
        UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"Releases returned by API: {releaseCount}");

        foreach (var releaseElement in doc.RootElement.EnumerateArray())
        {
            try
            {
                var manifest = ParseRelease(releaseElement, channel);
                if (manifest != null)
                {
                    UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"Returning manifest for {manifest.Version} (channel={manifest.Channel}, url={manifest.DownloadUrl})");
                    return manifest;
                }
            }
            catch (Exception ex)
            {
                UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"Exception parsing release: {ex.Message}");
                Debug.WriteLine($"[GitHubReleaseProvider] Skipping invalid release: {ex.Message}");
                continue;
            }
        }

        UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"REJECTION: No release matched requested channel={channel} after parsing all {releaseCount} releases.");
        return null;
    }

    private UpdateManifest? ParseRelease(JsonElement release, ReleaseChannel channel)
    {
        var tagName = release.TryGetProperty("tag_name", out var tagNameElem) ? tagNameElem.GetString() : "UNKNOWN";
        bool isPrerelease = release.TryGetProperty("prerelease", out var prereleaseElem) && prereleaseElem.GetBoolean();
        bool isDraft = release.TryGetProperty("draft", out var draftElem) && draftElem.GetBoolean();

        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Analyzing: tag='{tagName}' prerelease={isPrerelease} draft={isDraft} requestedChannel={channel}");
        Debug.WriteLine($"[GitHubReleaseProvider] Analyzing release: {tagName}. Prerelease: {isPrerelease}, Draft: {isDraft}");

        if (isDraft)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: '{tagName}' is a draft.");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: Is Draft.");
            return null;
        }

        if (string.IsNullOrEmpty(tagName) || tagName == "UNKNOWN" || !SemanticVersion.TryParse(tagName, out var version))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: '{tagName}' could not be parsed as a SemanticVersion.");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: Could not parse as SemanticVersion.");
            return null;
        }
        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Parsed version: {version} (Major={version.Major}, Minor={version.Minor}, Patch={version.Patch}, PreRelease='{version.PreReleaseLabel}')");

        ReleaseChannel releaseChannel = ReleaseChannel.Stable;
        if (isPrerelease)
        {
            if (tagName.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
                tagName.Contains("rc", StringComparison.OrdinalIgnoreCase))
            {
                releaseChannel = ReleaseChannel.Preview;
            }
            else
            {
                releaseChannel = ReleaseChannel.Nightly;
            }
        }
        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Mapped release channel: {releaseChannel} (rule: prerelease+('beta'|'rc' in tag) => Preview, other prerelease => Nightly, else Stable)");

        if (channel == ReleaseChannel.Stable && releaseChannel != ReleaseChannel.Stable)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: requested channel is Stable but release '{tagName}' is mapped to {releaseChannel} (Stable only accepts Stable).");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: App is Stable, Release is {releaseChannel}");
            return null;
        }

        if (channel == ReleaseChannel.Preview && releaseChannel == ReleaseChannel.Nightly)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: requested channel is Preview but release '{tagName}' is mapped to Nightly (Preview rejects Nightly).");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: App is Preview, Release is Nightly");
            return null;
        }

        // Nightly accepts Nightly, Preview, and Stable (all channels)

        Debug.WriteLine($"[GitHubReleaseProvider] Successfully parsed {tagName} as {version}");

        var body = release.TryGetProperty("body", out var bodyElem)
            ? bodyElem.GetString()
            : null;

        var assets = release.TryGetProperty("assets", out var assetsElem)
            ? assetsElem.EnumerateArray()
            : default;

        string downloadUrl = string.Empty;
        string sha256Hash = string.Empty;
        long fileSizeBytes = 0;
        int assetCount = 0;
        string selectedAssetName = string.Empty;

        foreach (var asset in assets)
        {
            assetCount++;
            var name = asset.TryGetProperty("name", out var nameElem)
                ? nameElem.GetString()
                : null;

            if (!string.IsNullOrEmpty(name) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                selectedAssetName = name ?? string.Empty;
                Debug.WriteLine($"[GitHubReleaseProvider] Found executable asset: {name}");
                downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElem)
                    ? urlElem.GetString() ?? string.Empty
                    : string.Empty;
                fileSizeBytes = asset.TryGetProperty("size", out var sizeElem) ? sizeElem.GetInt64() : 0;

                var bodyText = body ?? string.Empty;
                var safeName = System.Text.RegularExpressions.Regex.Escape(name!);
                var shaMatch = System.Text.RegularExpressions.Regex.Match(
                    bodyText,
                    $@"([a-fA-F0-9]{{64}})\s+{safeName}",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (shaMatch.Success && shaMatch.Groups.Count > 1)
                {
                    sha256Hash = shaMatch.Groups[1].Value.ToLowerInvariant();
                    UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Asset selected: '{name}' size={fileSizeBytes} url={downloadUrl}");
                    UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"SHA256 parsed from body: {sha256Hash}");
                    Debug.WriteLine($"[GitHubReleaseProvider] Found SHA256 in release body for {name}");
                }
                else
                {
                    UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Asset selected: '{name}' size={fileSizeBytes} url={downloadUrl}");
                    UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"SHA256 NOT FOUND in body for '{name}' (will be left empty).");
                    Debug.WriteLine($"[GitHubReleaseProvider] Warning: Could not find SHA256 in body for {name}");
                }
                break;
            }
        }
        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Total assets enumerated: {assetCount} | Selected: '{selectedAssetName}'");

        if (string.IsNullOrEmpty(downloadUrl))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: No .exe asset found among {assetCount} asset(s) for '{tagName}'.");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: No .exe asset found.");
            return null;
        }

        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"ACCEPTED: '{tagName}' parsed to {version} on channel {releaseChannel}");
        Debug.WriteLine($"[GitHubReleaseProvider] Returning valid manifest for {tagName}");

        return new UpdateManifest(
            Version: version,
            Channel: releaseChannel,
            ReleaseNotes: body ?? string.Empty,
            DownloadUrl: downloadUrl,
            Sha256Hash: sha256Hash,
            FileSizeBytes: fileSizeBytes,
            MinSupportedVersion: new SemanticVersion(1, 0, 0),
            IsRollbackEligible: false
        );
    }

    public void Dispose()
    {
        // _httpClient is injected and managed externally, do not dispose here
    }
}