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
        public string ResponseJson { get; set; } = "[]";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task GetLatestManifest_PrereleaseFlagTrueWithStableTag_IsNotTreatedAsStable()
    {
        const string releasesJson = """
        [
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
        ]
        """;

        var handler = new FakeHttpMessageHandler { ResponseJson = releasesJson };
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, "test-owner", "test-repo");

        var manifest = await provider.GetLatestManifestAsync(ReleaseChannel.Stable);

        Assert.Null(manifest);
    }
}
