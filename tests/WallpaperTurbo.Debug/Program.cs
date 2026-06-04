// Temporary diagnostic harness for Phase C update detection failure investigation.
// Traces every layer of the detection pipeline against the real GitHub API.
// No production code is modified by this file.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Services;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("============================================================");
        Console.WriteLine(" Wallpaper Turbo - Updater Detection Failure Diagnostics");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        // ============================================================
        // STEP 1: SemanticVersion parse + compare
        // ============================================================
        Console.WriteLine("--- STEP 1: SemanticVersion runtime verification ---");
        var installedRaw = "v1.2.1-beta.2";
        var remoteRaw   = "v1.2.1-rc.1";

        bool pi = SemanticVersion.TryParse(installedRaw, out var installed);
        bool pr = SemanticVersion.TryParse(remoteRaw, out var remote);

        Console.WriteLine($"  Input (installed):   {installedRaw}");
        Console.WriteLine($"    TryParse:          {pi}");
        if (pi) Console.WriteLine($"    Parsed:            {installed}  (Major={installed.Major}, Minor={installed.Minor}, Patch={installed.Patch}, PreRelease='{installed.PreReleaseLabel}')");

        Console.WriteLine($"  Input (remote):      {remoteRaw}");
        Console.WriteLine($"    TryParse:          {pr}");
        if (pr) Console.WriteLine($"    Parsed:            {remote}  (Major={remote.Major}, Minor={remote.Minor}, Patch={remote.Patch}, PreRelease='{remote.PreReleaseLabel}')");

        if (pi && pr)
        {
            int cmp = installed.CompareTo(remote);
            bool less = installed < remote;
            bool greater = installed > remote;
            bool equal = installed == remote;
            Console.WriteLine($"  installed.CompareTo(remote) = {cmp}");
            Console.WriteLine($"  installed < remote     = {less}");
            Console.WriteLine($"  installed > remote     = {greater}");
            Console.WriteLine($"  installed == remote    = {equal}");
            Console.WriteLine($"  -> UpdateService would set IsAvailable={less}");
        }
        Console.WriteLine();

        // ============================================================
        // STEP 2: UpdateService.GetCurrentVersion() reflection
        // ============================================================
        Console.WriteLine("--- STEP 2: UpdateService current version (via reflection) ---");
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var nameVer = asm.GetName().Version;
            Console.WriteLine($"  GetEntryAssembly:           {asm.GetName().Name}");
            Console.WriteLine($"  InformationalVersion attr:  {infoAttr?.InformationalVersion ?? "<none>"}");
            Console.WriteLine($"  AssemblyName.Version:       {nameVer?.ToString() ?? "<null>"}");
            if (infoAttr != null)
            {
                bool pv = SemanticVersion.TryParse(infoAttr.InformationalVersion, out var parsedFromInfo);
                Console.WriteLine($"  TryParse(info version):     {pv} -> {parsedFromInfo}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Reflection error: {ex.Message}");
        }
        Console.WriteLine();

        // ============================================================
        // STEP 3: GitHub API raw response
        // ============================================================
        var ownerRepoPairs = new (string Owner, string Repo)[]
        {
            ("WallpaperTurbo",     "WallpaperTurbo"),     // Hard-coded in App.xaml.cs (production)
            ("COSMO-ARNAB",        "Wallpaper-Turbo"),    // Hard-coded in test diagnostic scripts
        };

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WallpaperTurbo-Diag", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        foreach (var (owner, repo) in ownerRepoPairs)
        {
            Console.WriteLine($"--- STEP 3/4/5/6/7/8: Full pipeline for '{owner}/{repo}' ---");
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
            Console.WriteLine($"  GET {apiUrl}");

            using var response = await client.GetAsync(apiUrl, CancellationToken.None);
            Console.WriteLine($"  HTTP status: {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"  Body: {body}");
                Console.WriteLine($"  -> Provider would return null. UpdateService reports 'no update'.");
                Console.WriteLine();
                continue;
            }

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var releases = doc.RootElement.EnumerateArray().ToList();
            Console.WriteLine($"  Release count: {releases.Count}");

            for (int i = 0; i < releases.Count; i++)
            {
                var rel = releases[i];
                string tagName = rel.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() : "UNKNOWN";
                bool isPrerelease = rel.TryGetProperty("prerelease", out var preElem) && preElem.GetBoolean();
                bool isDraft = rel.TryGetProperty("draft", out var draftElem) && draftElem.GetBoolean();
                string body = rel.TryGetProperty("body", out var bodyElem) ? (bodyElem.GetString() ?? "") : "";

                Console.WriteLine($"  Release[{i}]: tag_name='{tagName}' prerelease={isPrerelease} draft={isDraft}");
                if (rel.TryGetProperty("assets", out var assetsElem))
                {
                    var assets = assetsElem.EnumerateArray().ToList();
                    Console.WriteLine($"    Asset count: {assets.Count}");
                    for (int a = 0; a < assets.Count; a++)
                    {
                        var asset = assets[a];
                        var an = asset.TryGetProperty("name", out var anElem) ? anElem.GetString() : null;
                        var au = asset.TryGetProperty("browser_download_url", out var auElem) ? auElem.GetString() : null;
                        var asz = asset.TryGetProperty("size", out var aszElem) ? aszElem.GetInt64() : 0;
                        bool isExe = !string.IsNullOrEmpty(an) && an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                        Console.WriteLine($"      Asset[{a}]: name='{an}' size={asz} isExe={isExe} url='{au}'");

                        if (isExe && an != null)
                        {
                            var safeName = System.Text.RegularExpressions.Regex.Escape(an);
                            var shaMatch = System.Text.RegularExpressions.Regex.Match(
                                body,
                                $@"([a-fA-F0-9]{{64}})\s+{safeName}",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (shaMatch.Success)
                            {
                                Console.WriteLine($"        SHA256 in body: {shaMatch.Groups[1].Value.ToLowerInvariant()}");
                            }
                            else
                            {
                                Console.WriteLine($"        SHA256 in body: <not found>");
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"    No assets field");
                }

                // Channel classification (mirrors GitHubReleaseProvider.ParseRelease)
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
                Console.WriteLine($"    Mapped release channel: {releaseChannel}");

                // Decision against each requested channel
                foreach (var requested in new[] { ReleaseChannel.Stable, ReleaseChannel.Preview, ReleaseChannel.Nightly })
                {
                    string decision;
                    if (requested == ReleaseChannel.Stable && releaseChannel != ReleaseChannel.Stable)
                        decision = "REJECTED (Stable only accepts Stable)";
                    else if (requested == ReleaseChannel.Preview && releaseChannel == ReleaseChannel.Nightly)
                        decision = "REJECTED (Preview does not accept Nightly)";
                    else
                        decision = "ACCEPTED";
                    Console.WriteLine($"      requested={requested,-8} -> {decision}");
                }

                // SemanticVersion parse + comparison
                bool parseOk = SemanticVersion.TryParse(tagName, out var parsed);
                Console.WriteLine($"    SemanticVersion.TryParse('{tagName}'): {parseOk}" + (parseOk ? $" -> {parsed}" : ""));
                if (parseOk && pi)
                {
                    bool isNewer = parsed > installed;
                    Console.WriteLine($"    Is '{tagName}' newer than installed '{installedRaw}'? {isNewer}");
                }
                Console.WriteLine();
            }
        }

        // ============================================================
        // STEP 9: Run the real UpdateService.CheckForUpdatesAsync
        // ============================================================
        Console.WriteLine("--- STEP 9: Real UpdateService.CheckForUpdatesAsync end-to-end ---");
        foreach (var (owner, repo) in ownerRepoPairs)
        {
            Console.WriteLine($"  Provider: '{owner}/{repo}'");
            var provider = new GitHubReleaseProvider(new HttpClient(
                new HttpClientHandler { UseCookies = false }), owner, repo);
            // The provider uses the injected HttpClient, so re-create with proper headers.
            // (This is only for diagnostic clarity; the real app uses the singleton with headers.)
            provider.Dispose();

            var diagClient = new HttpClient();
            diagClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WallpaperTurbo-Diag", "1.0"));
            diagClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var realProvider = new GitHubReleaseProvider(diagClient, owner, repo);
            var svc = new UpdateService(realProvider);

            foreach (var channel in new[] { ReleaseChannel.Stable, ReleaseChannel.Preview, ReleaseChannel.Nightly })
            {
                var (isAvail, m) = await svc.CheckForUpdatesAsync(channel);
                Console.WriteLine($"    channel={channel,-8} IsAvailable={isAvail} ManifestVersion={m?.Version.ToString() ?? "null"}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine(" End of diagnostic run");
        Console.WriteLine("============================================================");
    }
}
