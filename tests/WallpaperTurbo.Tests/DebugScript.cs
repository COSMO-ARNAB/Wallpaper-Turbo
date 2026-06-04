using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Services;

class Program
{
    static async Task Main(string[] args)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WallpaperTurbo-Test", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var provider = new GitHubReleaseProvider(client, "COSMO-ARNAB", "Wallpaper-Turbo");

        Console.WriteLine("Checking Stable...");
        var manifest = await provider.GetLatestManifestAsync(ReleaseChannel.Stable);
        Console.WriteLine($"Stable Result: {manifest?.Version?.ToString() ?? "null"}");

        Console.WriteLine("Checking Preview (Beta)...");
        manifest = await provider.GetLatestManifestAsync(ReleaseChannel.Preview);
        Console.WriteLine($"Preview Result: {manifest?.Version?.ToString() ?? "null"}");

        Console.WriteLine("Checking Nightly (Dev)...");
        manifest = await provider.GetLatestManifestAsync(ReleaseChannel.Nightly);
        Console.WriteLine($"Nightly Result: {manifest?.Version?.ToString() ?? "null"}");
    }
}
