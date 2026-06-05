using System.Net;
using System.Net.Http;
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
        public string ResponseJson { get; set; } = "[]";
        public string? LastRequestUri { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri?.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GetLatestManifest_HighestVersionWins_RegardlessOfGitHubOrder()
    {
        // Two Stable releases. v1.0.5 is listed FIRST in the JSON (simulating
        // GitHub's date-descending order with a re-publish / later update),
        // v2.0.0 is listed SECOND. The test MUST NOT depend on GitHub's
        // ordering — the fix must select v2.0.0 because it's the higher
        // SemanticVersion.
        const string releasesJson = """
        [
            {
                "id": 1,
                "tag_name": "v1.0.5",
                "prerelease": false,
                "draft": false,
                "body": "SHA256: 1111111111111111111111111111111111111111111111111111111111111111 Wallpaper_Turbo_Setup.exe",
                "published_at": "2026-06-05T00:00:00Z",
                "created_at": "2026-06-05T00:00:00Z",
                "updated_at": "2026-06-05T00:00:00Z",
                "assets": [
                    {
                        "name": "Wallpaper_Turbo_Setup.exe",
                        "browser_download_url": "https://example.invalid/v1.0.5/setup.exe",
                        "size": 1000
                    }
                ]
            },
            {
                "id": 2,
                "tag_name": "v2.0.0",
                "prerelease": false,
                "draft": false,
                "body": "SHA256: 2222222222222222222222222222222222222222222222222222222222222222 Wallpaper_Turbo_Setup.exe",
                "published_at": "2026-06-01T00:00:00Z",
                "created_at": "2026-06-01T00:00:00Z",
                "updated_at": "2026-06-01T00:00:00Z",
                "assets": [
                    {
                        "name": "Wallpaper_Turbo_Setup.exe",
                        "browser_download_url": "https://example.invalid/v2.0.0/setup.exe",
                        "size": 2000
                    }
                ]
            }
        ]
        """;

        var handler = new FakeHttpMessageHandler { ResponseJson = releasesJson };
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, "test-owner", "test-repo");

        var manifest = await provider.GetLatestManifestAsync(ReleaseChannel.Stable);

        Assert.NotNull(manifest);
        Assert.Equal(new SemanticVersion(2, 0, 0), manifest!.Version);
        Assert.Equal("https://example.invalid/v2.0.0/setup.exe", manifest.DownloadUrl);
        Assert.Equal(ReleaseChannel.Stable, manifest.Channel);
    }

    [Fact]
    public async Task GetLatestManifest_RequestUrl_ContainsPerPage100()
    {
        var handler = new FakeHttpMessageHandler { ResponseJson = "[]" };
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, "test-owner", "test-repo");

        await provider.GetLatestManifestAsync(ReleaseChannel.Stable);

        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("per_page=100", handler.LastRequestUri!);
    }

    [Fact]
    public async Task GetLatestManifest_SingleRelease_ReturnsIt()
    {
        const string releasesJson = """
        [
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
                    }
                ]
            }
        ]
        """;

        var handler = new FakeHttpMessageHandler { ResponseJson = releasesJson };
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, "test-owner", "test-repo");

        var manifest = await provider.GetLatestManifestAsync(ReleaseChannel.Stable);

        Assert.NotNull(manifest);
        Assert.Equal(new SemanticVersion(1, 2, 0), manifest!.Version);
    }
}
