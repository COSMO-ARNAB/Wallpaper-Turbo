// WallpaperSessionManager.cs - Manages active wallpaper sessions in Wallpaper Turbo, allowing for the addition of new sessions and the shutdown of all sessions when needed.
using System.Collections.Generic;

namespace WallpaperTurbo.Core.Wallpaper;

public sealed class WallpaperSessionManager
{
    private readonly List<WallpaperSession> _sessions =
        new();

    public IReadOnlyList<WallpaperSession> Sessions =>
        _sessions;

    public void AddSession(
        WallpaperSession session)
    {
        _sessions.Add(session);
    }

    public void ShutdownAll()
    {
        foreach (var session in _sessions)
        {
            session.Shutdown();
        }

        _sessions.Clear();
    }
}