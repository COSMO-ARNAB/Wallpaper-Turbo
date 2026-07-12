using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Tests;

public class GitHubReleaseProviderPrereleaseFlagTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public string ReleaseJson { get; set; } = "{}";
        public string UpdateJson { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    (request.RequestUri?.ToString() ?? string.Empty).Contains("update.json", StringComparison.OrdinalIgnoreCase)
                        ? UpdateJson
                        : ReleaseJson,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    [Fact]
    public async Task GetLatestManifest_PrereleaseFlagTrueWithStableTag_IsNotTreatedAsStable()
    {
        const string releaseJson = """
        {
            "id": 1,
            "tag_name": "v2.0.0",
            "prerelease": true,
            "draft": false,
            "body": "SHA256: 1111111111111111111111111111111111111111111111111111111111111111 Wallpaper_Turbo_Setup.exe",
            "published_at": "2026-06-05T00:00:00Z",
            "created_at": "2026-06-05T00:00:00Z",
            "updated_at": "2026-06-05T00:00:00Z",
            "assets": [
                {
                    "name": "Wallpaper_Turbo_Setup.exe",
                    "browser_download_url": "https://example.invalid/v2.0.0/setup.exe",
                    "size": 1000
                },
                {
                    "name": "update.json",
                    "browser_download_url": "https://example.invalid/v2.0.0/update.json",
                    "size": 200
                }
            ]
        }
        """;

        const string updateJson = """
        {
            "schema_version": 1,
            "generated_at": "2026-06-05T00:00:00Z",
            "version": "2.0.0",
            "channel": "stable",
            "installer_filename": "Wallpaper_Turbo_Setup.exe",
            "download_url": "https://example.invalid/v2.0.0/setup.exe",
            "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
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

        Assert.Null(manifest);
    }
}
