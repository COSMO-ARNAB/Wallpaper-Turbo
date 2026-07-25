using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

/// <summary>
/// Exercises the startup coordinator's wallpaper-selection resolution — specifically the
/// recent-history fallback. The launch/adoption paths touch a live AppRunner process and
/// named-pipe IPC, so they are not unit-testable here; the pure resolution logic is
/// extracted into ResolveWallpaperToLaunch / FindMostRecentlyUsed so it can be driven
/// with a temp history file and no real engine.
/// </summary>
public class WallpaperStartupCoordinatorFallbackTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly string[] KnownIds =
    {
        "11111111-0000-0000-0000-000000000001",
        "22222222-0000-0000-0000-000000000002",
        "33333333-0000-0000-0000-000000000003",
    };

    private static List<WallpaperEntry> BuildLibrary()
    {
        var list = new List<WallpaperEntry>();
        foreach (var id in KnownIds)
        {
            list.Add(new WallpaperEntry
            {
                Id = id,
                Title = "wp-" + id[..4],
                Video = "ignored.mp4",
                Thumbnail = string.Empty
            });
        }
        return list;
    }

    private static string WriteHistoryFile(params string[] ids)
    {
        var path = Path.Combine(Path.GetTempPath(), "WallpaperTurbo.Tests", Guid.NewGuid().ToString("N"), "recent_history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(ids));
        return path;
    }

    // ── Trusted-id path ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Uses_Trusted_LastActiveWallpaperId_When_RememberLast_On()
    {
        var library = BuildLibrary();
        var trustedId = KnownIds[2]; // not the first entry, not the first history entry

        var choice = WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
            library, trustedId, rememberLast: true, historyPath: "does-not-exist.json");

        Assert.Equal(trustedId, choice.Id);
    }

    [Fact]
    public void Resolve_Ignores_Trusted_Id_When_RememberLast_Off()
    {
        // Remember-last off => the trusted id is NOT consulted; falls through to history,
        // then to first library entry. With no history file present it must land on [0].
        var library = BuildLibrary();

        var choice = WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
            library, KnownIds[2], rememberLast: false, historyPath: "does-not-exist.json");

        Assert.Equal(KnownIds[0], choice.Id);
    }

    [Fact]
    public void Resolve_Falls_Back_To_Recent_History_When_No_Trusted_Id()
    {
        // No trusted id. recent_history.json lists [2] before [1]. The coordinator must
        // restore the genuinely most-recently-used wallpaper — [2] — NOT the first library
        // entry ([0]) and NOT a hardcoded name.
        var library = BuildLibrary();
        var historyPath = WriteHistoryFile(KnownIds[2], KnownIds[1], KnownIds[0]);

        try
        {
            var choice = WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
                library, lastActiveId: null, rememberLast: true, historyPath: historyPath);

            Assert.Equal(KnownIds[2], choice.Id);
        }
        finally
        {
            Cleanup(historyPath);
        }
    }

    [Fact]
    public void Resolve_Skips_Stale_History_Entries_That_No_Longer_Exist()
    {
        // History points at an id that was deleted from the library, then a still-valid id.
        // The fallback must skip the stale entry and restore the valid one, not bail to [0].
        var library = BuildLibrary();
        var staleId = "deadbeef-0000-0000-0000-000000000000";
        var historyPath = WriteHistoryFile(staleId, KnownIds[1]);

        try
        {
            var choice = WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
                library, lastActiveId: null, rememberLast: true, historyPath: historyPath);

            Assert.Equal(KnownIds[1], choice.Id);
        }
        finally
        {
            Cleanup(historyPath);
        }
    }

    [Fact]
    public void Resolve_Falls_Back_To_First_Library_Entry_When_History_Empty()
    {
        // No trusted id, no usable history — last resort is the first library entry.
        // This is the only place [0] is chosen, and it's by position, not by name.
        var library = BuildLibrary();
        var historyPath = WriteHistoryFile(); // empty array

        try
        {
            var choice = WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
                library, lastActiveId: null, rememberLast: true, historyPath: historyPath);

            Assert.Equal(KnownIds[0], choice.Id);
        }
        finally
        {
            Cleanup(historyPath);
        }
    }

    [Fact]
    public void Resolve_Falls_Back_To_First_Library_Entry_When_History_File_Missing()
    {
        var library = BuildLibrary();

        var choice = WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
            library, lastActiveId: null, rememberLast: true, historyPath: Path.Combine(Path.GetTempPath(), "definitely-missing.json"));

        Assert.Equal(KnownIds[0], choice.Id);
    }

    [Fact]
    public void Resolve_Does_Not_Consult_History_When_RememberLast_Off()
    {
        // Even with a history file present, remember-last off => first library entry.
        var library = BuildLibrary();
        var historyPath = WriteHistoryFile(KnownIds[2]);

        try
        {
            var choice = WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
                library, lastActiveId: null, rememberLast: false, historyPath: historyPath);

            Assert.Equal(KnownIds[0], choice.Id);
        }
        finally
        {
            Cleanup(historyPath);
        }
    }

    // ── Empty-library guard ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_Throws_When_Library_Empty()
    {
        var emptyLibrary = new List<WallpaperEntry>();

        Assert.Throws<ArgumentException>(() =>
            WallpaperStartupCoordinator.ResolveWallpaperToLaunch(
                emptyLibrary, lastActiveId: null, rememberLast: true, historyPath: "ignored.json"));
    }

    // ── Direct FindMostRecentlyUsed coverage ─────────────────────────────────

    [Fact]
    public void FindMostRecentlyUsed_Returns_First_Resolved_Entry_In_Order()
    {
        var library = BuildLibrary();
        var historyPath = WriteHistoryFile(KnownIds[1], KnownIds[2]);

        try
        {
            var wp = WallpaperStartupCoordinator.FindMostRecentlyUsed(library, historyPath);
            Assert.NotNull(wp);
            Assert.Equal(KnownIds[1], wp!.Id);
        }
        finally
        {
            Cleanup(historyPath);
        }
    }

    [Fact]
    public void FindMostRecentlyUsed_Returns_Null_When_No_Entry_Resolves()
    {
        var library = BuildLibrary();
        var historyPath = WriteHistoryFile("stale-1", "stale-2");

        try
        {
            var wp = WallpaperStartupCoordinator.FindMostRecentlyUsed(library, historyPath);
            Assert.Null(wp);
        }
        finally
        {
            Cleanup(historyPath);
        }
    }

    private static void Cleanup(string historyPath)
    {
        var dir = Path.GetDirectoryName(historyPath);
        if (dir != null && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
