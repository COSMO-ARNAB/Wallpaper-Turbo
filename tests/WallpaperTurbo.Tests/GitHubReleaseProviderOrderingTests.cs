using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Tests;

public class GitHubReleaseProviderOrderingTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public string ReleaseJson { get; set; } = "{}";
        public string UpdateJson { get; set; } = "{}";
        public List<string> RequestUris { get; } = new();
        public List<string?> IfNoneMatchHeaders { get; } = new();
        public int CallCount { get; private set; }
        public bool UseCaching { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            IfNoneMatchHeaders.Add(request.Headers.IfNoneMatch.Count > 0 ? request.Headers.IfNoneMatch.ToString() : null);

            if (UseCaching && request.Headers.IfNoneMatch.Count > 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var uriStr = request.RequestUri?.ToString() ?? string.Empty;
            var content = uriStr.Contains("update.json", StringComparison.OrdinalIgnoreCase)
                ? UpdateJson
                : ReleaseJson;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("ETag", "\"etag-1\"");
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GetLatestManifest_StableUsesLatestEndpoint()
    {
        const string releaseJson = """
        {
            "id": 1,
            "tag_name": "v1.2.0",
            "prerelease": false,
            "draft": false,
            "body": "SHA256: 3333333333333333333333333333333333333333333333333333333333333333 Wallpaper_Turbo_Setup.exe",
            "published_at": "2026-06-01T00:00:00Z",
            "created_at": "2026-06-01T00:00:00Z",
            "updated_at": "2026-06-01T00:00:00Z",
            "assets": [
                {
                    "name": "Wallpaper_Turbo_Setup.exe",
                    "browser_download_url": "https://example.invalid/v1.2.0/setup.exe",
                    "size": 1000
                },
                {
                    "name": "update.json",
                    "browser_download_url": "https://example.invalid/v1.2.0/update.json",
                    "size": 200
                }
            ]
        }
        """;

        const string updateJson = """
        {
            "schema_version": 1,
            "generated_at": "2026-06-01T00:00:00Z",
            "version": "1.2.0",
            "channel": "stable",
            "installer_filename": "Wallpaper_Turbo_Setup.exe",
            "download_url": "https://example.invalid/v1.2.0/setup.exe",
            "sha256": "3333333333333333333333333333333333333333333333333333333333333333",
            "file_size_bytes": 1000,
            "min_supported_version": "1.0.0",
            "min_signature_required": "authenticode",
            "rollback_eligible": false
        }
        """;

        var handler = new FakeHttpMessageHandler { ReleaseJson = releaseJson, UpdateJson = updateJson };
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, "test-owner", "test-repo");

        var manifest = await provider.GetLatestManifestAsync(ReleaseChannel.Stable);

        Assert.NotNull(manifest);
        Assert.Equal(new SemanticVersion(1, 2, 0), manifest!.Version);
        Assert.Equal("https://example.invalid/v1.2.0/setup.exe", manifest.DownloadUrl);
        Assert.Contains("/releases/latest", handler.RequestUris[0]);
    }

    [Fact]
    public async Task GetLatestManifest_StableSendsIfNoneMatchOnCachedRepeatCall()
    {
        const string releaseJson = """
        {
            "id": 1,
            "tag_name": "v1.2.0",
            "prerelease": false,
            "draft": false,
            "body": "SHA256: 3333333333333333333333333333333333333333333333333333333333333333 Wallpaper_Turbo_Setup.exe",
            "published_at": "2026-06-01T00:00:00Z",
            "created_at": "2026-06-01T00:00:00Z",
            "updated_at": "2026-06-01T00:00:00Z",
            "assets": [
                {
                    "name": "Wallpaper_Turbo_Setup.exe",
                    "browser_download_url": "https://example.invalid/v1.2.0/setup.exe",
                    "size": 1000
                },
                {
                    "name": "update.json",
                    "browser_download_url": "https://example.invalid/v1.2.0/update.json",
                    "size": 200
                }
            ]
        }
        """;

        const string updateJson = """
        {
            "schema_version": 1,
            "generated_at": "2026-06-01T00:00:00Z",
            "version": "1.2.0",
            "channel": "stable",
            "installer_filename": "Wallpaper_Turbo_Setup.exe",
            "download_url": "https://example.invalid/v1.2.0/setup.exe",
            "sha256": "3333333333333333333333333333333333333333333333333333333333333333",
            "file_size_bytes": 1000,
            "min_supported_version": "1.0.0",
            "min_signature_required": "authenticode",
            "rollback_eligible": false
        }
        """;

        var handler = new FakeHttpMessageHandler { ReleaseJson = releaseJson, UpdateJson = updateJson };
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, "test-owner", "test-repo");

        var first = await provider.GetLatestManifestAsync(ReleaseChannel.Stable);
        var second = await provider.GetLatestManifestAsync(ReleaseChannel.Stable);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(4, handler.CallCount);
        Assert.Equal(first!.Version, second!.Version);
    }

    [Fact]
    public async Task GetLatestManifest_NonStableStillUsesPagedEndpoint()
    {
        var handler = new FakeHttpMessageHandler { ReleaseJson = "[]", UpdateJson = "{}" };
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, "test-owner", "test-repo");

        await provider.GetLatestManifestAsync(ReleaseChannel.Preview);

        Assert.NotEmpty(handler.RequestUris);
        Assert.Contains("/releases?per_page=100&page=1", handler.RequestUris[0]);
    }
}
