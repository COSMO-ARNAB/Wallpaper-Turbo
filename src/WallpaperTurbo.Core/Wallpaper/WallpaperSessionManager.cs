// WallpaperSessionManager.cs - Manages active wallpaper sessions in Wallpaper Turbo.
using System;
using System.Collections.Generic;

namespace WallpaperTurbo.Core.Wallpaper;

public sealed class WallpaperSessionManager : IDisposable
{
    private readonly List<WallpaperSession> _sessions = new();

    private bool _disposed;

    public IReadOnlyList<WallpaperSession> Sessions => _sessions;

    public void AddSession(
        WallpaperSession session)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(session);

        _sessions.Add(session);
    }

    public void ShutdownAll()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _sessions.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        ShutdownAll();
    }
}