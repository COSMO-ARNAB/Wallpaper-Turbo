                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Platform.Display;
using WallpaperTurbo.Platform.Shell;
using WallpaperTurbo.Platform.Rendering;
using WallpaperTurbo.Media.Players;
using WallpaperTurbo.Shared;

namespace WallpaperTurbo.Core.Wallpaper;

/// <summary>
/// Encapsulates the runtime handles and player references for a single monitor rendering loop.
/// </summary>
public sealed class MonitorSession : IDisposable
{
    private bool _disposed;

    public string StableId { get; }
    public MonitorInfo Monitor { get; set; }
    public IntPtr Hwnd { get; set; } // Parent Win32 window
    public IWallpaperPlayerProcess? Player { get; set; }
    public int ActiveWallpaperIndex { get; set; } = -1;
    public string? ActiveVideoPath { get; set; }

    public MonitorSession(string stableId, MonitorInfo monitor, IntPtr hwnd)
    {
        StableId = stableId;
        Monitor = monitor;
        Hwnd = hwnd;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Player != null)
        {
            try { Player.StopAsync().GetAwaiter().GetResult(); } catch { }
            try { Player.Dispose(); } catch { }
            Player = null;
        }

        if (Hwnd != IntPtr.Zero)
        {
            try { NativeRenderWindow.Shutdown(Hwnd); } catch { }
            Hwnd = IntPtr.Zero;
        }
    }
}

/// <summary>
/// Manages the visual sessions mapped to active physical display screens.
/// </summary>
public sealed class MonitorSessionManager : IDisposable
{
    private readonly Dictionary<string, MonitorSession> _activeSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    public IReadOnlyCollection<MonitorSession> ActiveSessions
    {
        get
        {
            lock (_lock)
            {
                return _activeSessions.Values.ToArray();
            }
        }
    }

    public MonitorSession? GetSession(string stableId)
    {
        lock (_lock)
        {
            return _activeSessions.TryGetValue(stableId, out var session) ? session : null;
        }
    }

    public async Task<bool> StartSessionAsync(
        MonitorInfo monitor,
        string stableId,
        string videoPath,
        WallpaperConfig config,
        IShellCompositionService compositionService,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 1. Teardown existing session if present
        await StopSessionAsync(stableId).ConfigureAwait(false);

        Console.WriteLine($"[MonitorSessionManager] Starting session for screen {monitor.DeviceName} (ID: {stableId})");

        IntPtr parentHwnd = IntPtr.Zero;
        IWallpaperPlayerProcess? player = null;

        try
        {
            // 2. Create borderless parent host window
            parentHwnd = await NativeRenderWindow.CreateAsync(monitor).ConfigureAwait(false);
            if (parentHwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create parent host window.");
            }

            // 3. Attach host window to shell WorkerW layer
            compositionService.Initialize(parentHwnd);
            bool attached = compositionService.Nest(parentHwnd, monitor);
            if (!attached)
            {
                throw new InvalidOperationException("Failed to attach parent window to desktop shell.");
            }
            compositionService.Stabilize(parentHwnd, monitor);

            // 4. Create and start isolated player process
            string renderer = config.Renderer.ActiveRenderer;
            player = WallpaperPlayerFactory.CreatePlayer(renderer, config);

            try
            {
                await player.StartAsync(videoPath, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[MonitorSessionManager] Primary renderer {renderer} failed: {ex.Message}. Falling back to: {config.Renderer.FallbackRenderer}");
                Console.ResetColor();

                try { await player.StopAsync().ConfigureAwait(false); } catch { }
                player.Dispose();

                player = WallpaperPlayerFactory.CreatePlayer(config.Renderer.FallbackRenderer, config);
                await player.StartAsync(videoPath, token).ConfigureAwait(false);
            }

            // 5. Parent player process window to host window
            compositionService.Nest(player.WindowHandle, monitor);
            compositionService.Stabilize(player.WindowHandle, monitor);

            // 6. Start playback
            player.Play();

            // 7. Save session details
            var session = new MonitorSession(stableId, monitor, parentHwnd)
            {
                Player = player,
                ActiveVideoPath = videoPath
            };

            lock (_lock)
            {
                _activeSessions[stableId] = session;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MonitorSessionManager] Error starting session on {monitor.DeviceName}: {ex.Message}");
            
            // Clean up resources on failure
            if (player != null)
            {
                try { await player.StopAsync().ConfigureAwait(false); } catch { }
                player.Dispose();
            }
            if (parentHwnd != IntPtr.Zero)
            {
                NativeRenderWindow.Shutdown(parentHwnd);
            }
            return false;
        }
    }

    public async Task StopSessionAsync(string stableId)
    {
        MonitorSession? session = null;
        lock (_lock)
        {
            if (_activeSessions.Remove(stableId, out var active))
            {
                session = active;
            }
        }

        if (session != null)
        {
            Console.WriteLine($"[MonitorSessionManager] Stopping session for screen ID: {stableId}");
            await Task.Run(() => session.Dispose()).ConfigureAwait(false);
        }
    }

    public void PlaySession(string stableId)
    {
        lock (_lock)
        {
            if (_activeSessions.TryGetValue(stableId, out var session))
            {
                session.Player?.Play();
            }
        }
    }

    public void PauseSession(string stableId)
    {
        lock (_lock)
        {
            if (_activeSessions.TryGetValue(stableId, out var session))
            {
                session.Player?.Pause();
            }
        }
    }

    public void ShutdownAll()
    {
        MonitorSession[] sessionsCopy;
        lock (_lock)
        {
            sessionsCopy = _activeSessions.Values.ToArray();
            _activeSessions.Clear();
        }

        foreach (var s in sessionsCopy)
        {
            try { s.Dispose(); } catch { }
        }
    }

    public void HandleTopologyChanged(IReadOnlyList<MonitorInfo> freshMonitors, IShellCompositionService compositionService)
    {
        lock (_lock)
        {
            // Update coordinates of existing sessions, or shutdown removed screens
            var removedStableIds = new List<string>();

            foreach (var activeKvp in _activeSessions)
            {
                string stableId = activeKvp.Key;
                MonitorSession session = activeKvp.Value;

                // Match by DeviceName first
                var matchedMonitor = freshMonitors.FirstOrDefault(m => string.Equals(m.DeviceName, session.Monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
                
                // Fallback: match by EDID hash builder match
                if (matchedMonitor == null)
                {
                    matchedMonitor = freshMonitors.FirstOrDefault(m => string.Equals(MonitorIdentityBuilder.GetPersistentId(m.DeviceName), stableId, StringComparison.OrdinalIgnoreCase));
                }

                if (matchedMonitor != null)
                {
                    // Resize existing parent and player windows to match new screen dimensions
                    session.Monitor = matchedMonitor;
                    
                    if (session.Hwnd != IntPtr.Zero)
                    {
                        compositionService.Nest(session.Hwnd, matchedMonitor);
                        compositionService.Stabilize(session.Hwnd, matchedMonitor);
                    }
                    if (session.Player != null && session.Player.WindowHandle != IntPtr.Zero)
                    {
                        compositionService.Nest(session.Player.WindowHandle, matchedMonitor);
                        compositionService.Stabilize(session.Player.WindowHandle, matchedMonitor);
                    }
                }
                else
                {
                    removedStableIds.Add(stableId);
                }
            }

            foreach (string removedId in removedStableIds)
            {
                if (_activeSessions.Remove(removedId, out var s))
                {
                    try { s.Dispose(); } catch { }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ShutdownAll();
    }
}
