// WallpaperSessionManager.cs - Manages active wallpaper sessions in Wallpaper Turbo.
using System;
using System.Collections.Generic;

namespace WallpaperTurbo.Core.Wallpaper;

public sealed class WallpaperSessionManager : IDisposable
{
    private readonly List<WallpaperSession> _sessions = new();
    private readonly object _lock = new();

    private bool _disposed;

    public IReadOnlyList<WallpaperSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _sessions.ToArray();
            }
        }
    }

    public void AddSession(
        WallpaperSession session)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            _sessions.Add(session);
        }
    }

    public void ReplaceSession(
        WallpaperSession oldSession,
        WallpaperSession newSession)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(oldSession);
        ArgumentNullException.ThrowIfNull(newSession);

        lock (_lock)
        {
            int index = _sessions.IndexOf(oldSession);
            if (index >= 0)
            {
                _sessions[index] = newSession;
            }
            else
            {
                _sessions.Add(newSession);
            }
        }
    }

    public void RemoveSession(
        WallpaperSession session)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            _sessions.Remove(session);
        }
    }

    public void ShutdownAll()
    {
        List<WallpaperSession> sessionsCopy;
        lock (_lock)
        {
            sessionsCopy = new List<WallpaperSession>(_sessions);
            _sessions.Clear();
        }

        foreach (var session in sessionsCopy)
        {
            try
            {
                session.Dispose();
            }
            catch
            {
                // Defensive: ensure other sessions are still disposed
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        ShutdownAll();
    }
}