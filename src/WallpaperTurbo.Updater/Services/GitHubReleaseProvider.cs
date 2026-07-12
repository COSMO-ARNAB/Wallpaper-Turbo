using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Models;

namespace WallpaperTurbo.Updater.Services;

public sealed class GitHubReleaseProvider : IUpdateSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly Dictionary<string, CachedResponse> _responseCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _responseCacheLock = new();

    private const string InstallerFileName = "Wallpaper_Turbo_Setup.exe";
    private const string UpdateJsonAssetName = "update.json";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class CachedResponse
    {
        public string? ETag { get; init; }
        public string Body { get; init; } = string.Empty;
    }

    public GitHubReleaseProvider(HttpClient httpClient, string owner, string repo)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task<UpdateManifest?> GetLatestManifestAsync(ReleaseChannel channel, CancellationToken cancellationToken = default)
    {
        if (channel == ReleaseChannel.Stable)
        {
            var apiUrl = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
            UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"GET {apiUrl} | requested channel={channel}");

            using var doc = await GetJsonDocumentAsync(apiUrl, cancellationToken);
            if (doc == null)
            {
                UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"REJECTION: Failed to fetch latest release from {apiUrl}");
                return null;
            }

            return await ParseReleaseAsync(doc.RootElement, channel, cancellationToken);
        }

        var validManifests = new List<UpdateManifest>();
        int releaseCount = 0;
        for (int page = 1; ; page++)
        {
            string apiUrl = $"https://api.github.com/repos/{_owner}/{_repo}/releases?per_page=100&page={page}";
            UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"GET {apiUrl} | requested channel={channel}");

            using var doc = await GetJsonDocumentAsync(apiUrl, cancellationToken);
            if (doc == null)
            {
                UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"REJECTION: Failed to fetch releases page {page} from {apiUrl}");
                return null;
            }

            int pageCount = 0;
            foreach (var _ in doc.RootElement.EnumerateArray()) pageCount++;
            releaseCount += pageCount;
            UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"Releases returned by API page {page}: {pageCount}");

            foreach (var releaseElement in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var manifest = await ParseReleaseAsync(releaseElement, channel, cancellationToken);
                    if (manifest != null)
                    {
                        validManifests.Add(manifest);
                    }
                }
                catch (Exception ex)
                {
                    UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"Exception parsing release: {ex.Message}");
                    Debug.WriteLine($"[GitHubReleaseProvider] Skipping invalid release: {ex.Message}");
                    continue;
                }
            }

            if (pageCount < 100)
            {
                break;
            }
        }

        UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"Valid manifests for channel={channel} after parsing all {releaseCount} releases: {validManifests.Count}");

        if (validManifests.Count == 0)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"REJECTION: No release matched requested channel={channel} after parsing all {releaseCount} releases.");
            return null;
        }

        var best = validManifests
            .OrderByDescending(m => m.Version, Comparer<SemanticVersion>.Default)
            .First();

        UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"Selected highest-version manifest: {best.Version} (channel={best.Channel}, url={best.DownloadUrl}, sigReq={best.MinSignatureRequirement}) out of {validManifests.Count} candidate(s)");
        return best;
    }

    private async Task<JsonDocument?> GetJsonDocumentAsync(string url, CancellationToken cancellationToken)
    {
        string? cachedEtag = null;
        string? cachedBody = null;

        lock (_responseCacheLock)
        {
            if (_responseCache.TryGetValue(url, out var cached))
            {
                cachedEtag = cached.ETag;
                cachedBody = cached.Body;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WallpaperTurbo", "1.0"));
        if (!string.IsNullOrWhiteSpace(cachedEtag))
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cachedEtag));
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"HTTP status: {(int)response.StatusCode} {response.StatusCode}");

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (cachedBody == null)
            {
                UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"REJECTION: 304 Not Modified for {url} but no cached body exists.");
                return null;
            }

            return JsonDocument.Parse(cachedBody);
        }

        if (!response.IsSuccessStatusCode)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.GetLatestManifest", $"REJECTION: Non-success HTTP status {(int)response.StatusCode} {response.StatusCode} from {url}");
            Debug.WriteLine($"[GitHubReleaseProvider] Failed to fetch releases: {response.StatusCode}");
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = JsonDocument.Parse(json);

        var responseEtag = response.Headers.ETag?.ToString();
        if (string.IsNullOrWhiteSpace(responseEtag) && response.Headers.TryGetValues("ETag", out var headerValues))
        {
            responseEtag = headerValues.FirstOrDefault();
        }
        if (!string.IsNullOrWhiteSpace(responseEtag))
        {
            lock (_responseCacheLock)
            {
                _responseCache[url] = new CachedResponse
                {
                    ETag = responseEtag,
                    Body = json
                };
            }
        }

        return document;
    }

    private async Task<UpdateManifest?> ParseReleaseAsync(JsonElement release, ReleaseChannel requestedChannel, CancellationToken cancellationToken)
    {
        var tagName = release.TryGetProperty("tag_name", out var tagNameElem) ? tagNameElem.GetString() : "UNKNOWN";
        bool isPrerelease = release.TryGetProperty("prerelease", out var prereleaseElem) && prereleaseElem.GetBoolean();
        bool isDraft = release.TryGetProperty("draft", out var draftElem) && draftElem.GetBoolean();

        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Analyzing: tag='{tagName}' prerelease={isPrerelease} draft={isDraft} requestedChannel={requestedChannel}");
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
        if (isPrerelease || !string.IsNullOrEmpty(version.PreReleaseLabel))
        {
            if (version.PreReleaseLabel.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
                version.PreReleaseLabel.Contains("rc", StringComparison.OrdinalIgnoreCase))
            {
                releaseChannel = ReleaseChannel.Preview;
            }
            else
            {
                releaseChannel = ReleaseChannel.Nightly;
            }
        }
        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"Mapped release channel: {releaseChannel} (rule: tag contains 'beta'|'rc' => Preview, other prerelease tag => Nightly, else Stable)");

        if (requestedChannel == ReleaseChannel.Stable && releaseChannel != ReleaseChannel.Stable)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: requested channel is Stable but release '{tagName}' is mapped to {releaseChannel} (Stable only accepts Stable).");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: App is Stable, Release is {releaseChannel}");
            return null;
        }

        if (requestedChannel == ReleaseChannel.Preview && releaseChannel == ReleaseChannel.Nightly)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: requested channel is Preview but release '{tagName}' is mapped to Nightly (Preview rejects Nightly).");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: App is Preview, Release is Nightly");
            return null;
        }

        // Nightly accepts Nightly, Preview, and Stable (all channels)

        Debug.WriteLine($"[GitHubReleaseProvider] Successfully parsed {tagName} as {version}");

        var assets = release.TryGetProperty("assets", out var assetsElem)
            ? assetsElem
            : default;

        string? updateJsonUrl = null;
        if (assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElem) ? nameElem.GetString() : null;
                if (string.Equals(name, UpdateJsonAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    updateJsonUrl = asset.TryGetProperty("browser_download_url", out var urlElem)
                        ? urlElem.GetString()
                        : null;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(updateJsonUrl))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: No update.json asset found for '{tagName}'.");
            Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: Missing update.json.");
            return null;
        }

        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"update.json asset found for '{tagName}' (url={updateJsonUrl})");
        var (manifestManifest, rejectionReason) = await TryFetchAndMapRemoteManifestAsync(updateJsonUrl!, releaseChannel, cancellationToken);
        if (manifestManifest != null)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"ACCEPTED: '{tagName}' manifest-driven path. hash={manifestManifest.Sha256Hash} size={manifestManifest.FileSizeBytes} sigReq={manifestManifest.MinSignatureRequirement} source=update.json");
            Debug.WriteLine($"[GitHubReleaseProvider] Returning manifest for {tagName} (source=update.json)");
            return manifestManifest;
        }

        UpdaterDiagnostic.Log("GitHubReleaseProvider.ParseRelease", $"REJECTION: update.json fetch/validate failed for '{tagName}'. reason='{rejectionReason ?? "unknown"}'");
        Debug.WriteLine($"[GitHubReleaseProvider] Rejected {tagName}: update.json validation failed: {rejectionReason}");
        return null;
    }

    private async Task<(UpdateManifest? Manifest, string? RejectionReason)> TryFetchAndMapRemoteManifestAsync(string downloadUrl, ReleaseChannel releaseChannel, CancellationToken cancellationToken)
    {
        string? rejectionReason = null;
        try
        {
            using var response = await _httpClient.GetAsync(downloadUrl, cancellationToken);
            UpdaterDiagnostic.Log("GitHubReleaseProvider.TryFetchRemote", $"GET {downloadUrl} -> {(int)response.StatusCode} {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                UpdaterDiagnostic.Log("GitHubReleaseProvider.TryFetchRemote", $"REJECTION: Non-success HTTP status {(int)response.StatusCode} from update.json URL");
                rejectionReason = $"http_{((int)response.StatusCode)}";
                return (null, rejectionReason);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            RemoteUpdateManifest? remote;
            try
            {
                remote = JsonSerializer.Deserialize<RemoteUpdateManifest>(json, ManifestJsonOptions);
            }
            catch (JsonException ex)
            {
                UpdaterDiagnostic.Log("GitHubReleaseProvider.TryFetchRemote", $"REJECTION: update.json failed to deserialize: {ex.Message}");
                Debug.WriteLine($"[GitHubReleaseProvider] update.json deserialization failed: {ex.Message}");
                rejectionReason = $"deserialize: {ex.Message}";
                return (null, rejectionReason);
            }
            if (remote == null)
            {
                UpdaterDiagnostic.Log("GitHubReleaseProvider.TryFetchRemote", "REJECTION: update.json deserialized to null");
                rejectionReason = "deserialize_null";
                return (null, rejectionReason);
            }

            var mapped = MapRemoteManifestToUpdateManifest(remote, releaseChannel);
            if (mapped == null)
            {
                rejectionReason = "validation_failed";
                return (null, rejectionReason);
            }
            return (mapped, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.TryFetchRemote", $"REJECTION: update.json fetch threw {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"[GitHubReleaseProvider] update.json fetch threw: {ex.Message}");
            rejectionReason = $"exception: {ex.GetType().Name}: {ex.Message}";
            return (null, rejectionReason);
        }
    }

    /// <summary>
    /// Validates a deserialized <see cref="RemoteUpdateManifest"/> and maps it to the
    /// runtime <see cref="UpdateManifest"/>. Returns null when any field fails
    /// validation so the caller can fall back to body-parsing.
    /// </summary>
    /// <param name="remote">The deserialized update.json contents.</param>
    /// <param name="releaseChannel">
    /// The authoritative channel derived from GitHub's <c>prerelease</c> flag in
    /// <see cref="ParseReleaseAsync"/>. Used to cross-validate the manifest's
    /// <c>channel</c> field and to default the signature requirement when the
    /// manifest leaves it unspecified.
    /// </param>
    public static UpdateManifest? MapRemoteManifestToUpdateManifest(RemoteUpdateManifest remote, ReleaseChannel releaseChannel)
    {
        if (remote == null) return null;

        if (remote.SchemaVersion != 1)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"REJECTION: schema_version={remote.SchemaVersion} (expected 1)");
            return null;
        }

        if (!string.Equals(remote.InstallerFilename, InstallerFileName, StringComparison.OrdinalIgnoreCase))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"REJECTION: installer_filename='{remote.InstallerFilename}' (expected '{InstallerFileName}')");
            return null;
        }

        if (string.IsNullOrEmpty(remote.Sha256) || remote.Sha256.Length != 64 || !Regex.IsMatch(remote.Sha256, "^[0-9a-fA-F]{64}$"))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"REJECTION: sha256 missing or not 64 hex chars (length={(remote.Sha256 ?? "").Length})");
            return null;
        }

        if (remote.FileSizeBytes <= 0)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"REJECTION: file_size_bytes={remote.FileSizeBytes} (expected > 0)");
            return null;
        }

        if (string.IsNullOrEmpty(remote.Version) || !SemanticVersion.TryParse(remote.Version, out var semver))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"REJECTION: version='{remote.Version}' could not be parsed as SemanticVersion");
            return null;
        }

        if (string.IsNullOrEmpty(remote.Channel) || !TryParseChannel(remote.Channel, out var channel))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"REJECTION: channel='{remote.Channel}' is not recognized (stable|preview|nightly)");
            return null;
        }

        if (channel != releaseChannel)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"REJECTION: update.json channel='{remote.Channel}' (parsed {channel}) does not match authoritative GitHub release channel={releaseChannel} (prerelease flag)");
            return null;
        }

        if (string.IsNullOrEmpty(remote.DownloadUrl))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", "REJECTION: download_url is empty");
            return null;
        }

        var minSupportedRaw = string.IsNullOrEmpty(remote.MinSupportedVersion) ? "1.0.0" : remote.MinSupportedVersion;
        SemanticVersion minSupported;
        if (!SemanticVersion.TryParse(minSupportedRaw, out minSupported))
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"WARN: min_supported_version='{minSupportedRaw}' could not be parsed; defaulting to 1.0.0");
            minSupported = new SemanticVersion(1, 0, 0);
        }

        var sigReq = ParseSignatureRequirement(remote.MinSignatureRequired, channel, out var sigWasDefaulted);
        if (sigWasDefaulted)
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"min_signature_required='{remote.MinSignatureRequired}' not recognized or absent; defaulting to {sigReq} for channel {channel}");
        }
        else
        {
            UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"min_signature_required from manifest = {sigReq} (channel default would have been {DefaultSignatureRequirementForChannel(channel)})");
        }

        UpdaterDiagnostic.Log("GitHubReleaseProvider.MapRemoteManifest", $"MAPPED: schema_version=1 version={semver} channel={channel} hash={remote.Sha256.ToLowerInvariant()} size={remote.FileSizeBytes} sigReq={sigReq} source=update.json");

        return new UpdateManifest(
            Version: semver,
            Channel: channel,
            ReleaseNotes: remote.ReleaseNotes ?? string.Empty,
            DownloadUrl: remote.DownloadUrl,
            Sha256Hash: remote.Sha256.ToLowerInvariant(),
            FileSizeBytes: remote.FileSizeBytes,
            MinSupportedVersion: minSupported,
            IsRollbackEligible: remote.RollbackEligible ?? false,
            MinSignatureRequirement: sigReq
        );
    }

    public static bool TryParseChannel(string raw, out ReleaseChannel channel)
    {
        channel = ReleaseChannel.Stable;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "stable":  channel = ReleaseChannel.Stable;  return true;
            case "preview": channel = ReleaseChannel.Preview; return true;
            case "nightly": channel = ReleaseChannel.Nightly; return true;
            default:        return false;
        }
    }

    public static SignatureRequirement ParseSignatureRequirement(string? raw, ReleaseChannel channel, out bool wasDefaulted)
    {
        wasDefaulted = false;
        if (string.IsNullOrWhiteSpace(raw))
        {
            wasDefaulted = true;
            return DefaultSignatureRequirementForChannel(channel);
        }
        switch (raw.Trim().ToLowerInvariant())
        {
            case "authenticode": return SignatureRequirement.Authenticode;
            case "sha256-only":  return SignatureRequirement.Sha256Only;
            default:
                wasDefaulted = true;
                return DefaultSignatureRequirementForChannel(channel);
        }
    }

    public static SignatureRequirement DefaultSignatureRequirementForChannel(ReleaseChannel channel)
    {
        switch (channel)
        {
            case ReleaseChannel.Stable:  return SignatureRequirement.Sha256Only;
            case ReleaseChannel.Preview: return SignatureRequirement.Sha256Only;
            case ReleaseChannel.Nightly: return SignatureRequirement.Sha256Only;
            default:                     return SignatureRequirement.Sha256Only;
        }
    }

    public void Dispose()
    {
        // _httpClient is injected and managed externally, do not dispose here
    }
}
