using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WallpaperTurbo.UI.Services;

/// <summary>
/// Centralized, production-hardened wallpaper hover preview service.
/// 
/// Architecture principles:
/// 1. Single global MediaPlayer (singleton reuse - no COM create/destroy churn).
/// 2. Session IDs: every StartPreview call increments _activeSessionId. Stale callbacks self-abort.
/// 3. Lock structure: _previewLock is held ONLY for setup/teardown state mutations.
///    It is NEVER held while awaiting async operations (MediaOpened, Task.Delay, etc.)
///    Holding a lock while awaiting = guaranteed UI freeze during mouse-leave contention.
/// 4. All media event callbacks dispatch via InvokeAsync (non-blocking, no Invoke).
/// 5. 500ms startup cooldown protects GPU decoder from overlapping sessions.
/// 6. 4-second startup watchdog via TaskCompletionSource + Task.WhenAny.
/// 7. 8-second inactivity watchdog resets on mouse move.
/// </summary>
public class WallpaperPreviewService : IWallpaperPreviewService
{
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mkv", ".gif" };
    private const long MaxPreviewFileSizeBytes = 150L * 1024 * 1024; // 150MB heuristic
    private const int DwellDelayMs = 1500;
    private const int WatchdogStartupMs = 4000;
    private const int WatchdogInactivityMs = 8000;
    private const int CooldownMs = 500;
    private const int ResetThrottleMs = 250;

    // Single lock only for short, synchronous state mutations.
    // NEVER await inside this lock - that causes mouse-leave deadlocks.
    private readonly object _stateLock = new();

    // Singleton MediaPlayer kept alive for app lifetime
    private MediaPlayer? _mediaPlayer;
    private VideoDrawing? _videoDrawing;

    // Active session tracking
    private long _activeSessionId = 0;
    private WallpaperEntry? _activeEntry;
    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _watchdogCts;

    // Startup watchdog TCS - set by OnMediaOpened/OnMediaFailed callbacks
    private volatile TaskCompletionSource<bool>? _sessionOpenedTcs;

    // Timing
    private DateTime _lastResetTime = DateTime.MinValue;
    private DateTime _lastPreviewStopTime = DateTime.MinValue;

    private static string LogPrefix(long sessionId) =>
        $"[{DateTime.UtcNow:HH:mm:ss.fff}][T{Thread.CurrentThread.ManagedThreadId}][S{sessionId}]";

    // ─────────────────────────────────────────────────────────────────────
    // Media Player Singleton (created once on UI thread, reused forever)
    // ─────────────────────────────────────────────────────────────────────
    private void EnsurePlayerCreated_UIThread()
    {
        // Must be called on UI thread
        if (_mediaPlayer == null)
        {
            _mediaPlayer = new MediaPlayer { Volume = 0, IsMuted = true };
            _mediaPlayer.MediaOpened += OnMediaOpened;
            _mediaPlayer.MediaEnded  += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
            Debug.WriteLine("[Preview] Singleton MediaPlayer created.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Media callbacks: ALWAYS async InvokeAsync, never blocking Invoke
    // ─────────────────────────────────────────────────────────────────────
    private void OnMediaOpened(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Debug.WriteLine($"[Preview] MediaOpened callback fired.");
            _sessionOpenedTcs?.TrySetResult(true);
        }, DispatcherPriority.Background);
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        Debug.WriteLine($"[Preview] MediaFailed: {e.ErrorException?.Message}");
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _sessionOpenedTcs?.TrySetResult(false);
        }, DispatcherPriority.Background);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            // Only loop if session is still active
            if (_mediaPlayer != null && _activeEntry != null)
            {
                try
                {
                    _mediaPlayer.Position = TimeSpan.Zero;
                    _mediaPlayer.Play();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Preview] Loop seek exception: {ex.Message}");
                }
            }
        }, DispatcherPriority.Background);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────
    public async Task StartPreviewAsync(WallpaperEntry entry)
    {
        if (entry == null) return;

        // Offload all checks, file I/O, and lock contention to a background thread pool thread.
        // This ensures the UI thread remains 100% responsive during mouse hover.
        await Task.Run(async () =>
        {
            if (DebugFlags.SafeDebugMode && !DebugFlags.EnableHoverPreviews)
            {
                Debug.WriteLine("[ISOLATE] StartPreviewAsync requested but bypassed via EnableHoverPreviews = false.");
                return;
            }

            DiagnosticsService.SetAction($"Hover Preview Start dwelling: '{entry.Title}'");

            // Fast, non-blocking pre-checks (no locks needed)
            string ext = Path.GetExtension(entry.Video ?? "").ToLowerInvariant();
            if (!Array.Exists(VideoExtensions, e => e == ext)) return;

            try
            {
                if (!File.Exists(entry.Video)) return;
                var info = new FileInfo(entry.Video);
                if (info.Length > MaxPreviewFileSizeBytes)
                {
                    Debug.WriteLine($"[Preview] Skip '{entry.Title}': exceeds 150MB heuristic.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preview] Pre-check error: {ex.Message}");
                return;
            }

            // Atomically claim a new session and cancel the previous one
            long sessionId;
            CancellationTokenSource sessionCts;
            lock (_stateLock)
            {
                sessionId = ++_activeSessionId;
                // Cancel previous dwell/watchdog immediately
                _activeCts?.Cancel();
                _activeCts?.Dispose();
                sessionCts = new CancellationTokenSource();
                _activeCts = sessionCts;
                _activeEntry = entry;
            }

            Debug.WriteLine($"{LogPrefix(sessionId)} StartPreview '{entry.Title}'");

            var token = sessionCts.Token;

            // 1.5-second dwell debounce (fully background, no locks held)
            try
            {
                await Task.Delay(DwellDelayMs, token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"{LogPrefix(sessionId)} Dwell cancelled.");
                return;
            }

            // Session still valid?
            if (token.IsCancellationRequested) return;
            lock (_stateLock)
            {
                if (_activeSessionId != sessionId) return;
            }

            DiagnosticsService.SetAction($"Hover Preview Dispatching to UI Thread: '{entry.Title}'");

            // Marshal the actual player initialization to the UI thread
            await Application.Current.Dispatcher.InvokeAsync(
                () => InitializePlayerOnUIThread(entry, sessionId, token),
                DispatcherPriority.Background);
        });
    }

    public async Task StopPreviewAsync()
    {
        // Immediately cancel active session so any in-flight dwell/watchdog aborts
        CancellationTokenSource? ctsToCancel;
        lock (_stateLock)
        {
            ctsToCancel = _activeCts;
            _activeCts = null;
            _activeSessionId++; // Invalidate current session
        }

        ctsToCancel?.Cancel();
        ctsToCancel?.Dispose();

        // If we're on the UI thread, tear down immediately
        if (Application.Current.Dispatcher.CheckAccess())
        {
            TearDownPlayer_UIThread();
        }
        else
        {
            await Application.Current.Dispatcher.InvokeAsync(
                TearDownPlayer_UIThread,
                DispatcherPriority.Background);
        }
    }

    public void ResetTimer()
    {
        // Throttled to once per ResetThrottleMs to avoid event flooding
        var now = DateTime.UtcNow;
        if ((now - _lastResetTime).TotalMilliseconds > ResetThrottleMs)
        {
            _lastResetTime = now;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player initialization - runs on UI thread, NO awaiting while holding locks
    // ─────────────────────────────────────────────────────────────────────
    private async void InitializePlayerOnUIThread(WallpaperEntry entry, long sessionId, CancellationToken token)
    {
        // Guard: still valid session?
        lock (_stateLock)
        {
            if (_activeSessionId != sessionId) return;
        }

        try
        {
            // 500ms cooldown to protect GPU decoder from overlapping sessions
            var elapsed = (DateTime.UtcNow - _lastPreviewStopTime).TotalMilliseconds;
            if (elapsed < CooldownMs)
            {
                int wait = CooldownMs - (int)elapsed;
                Debug.WriteLine($"{LogPrefix(sessionId)} Cooldown wait {wait}ms.");
                DiagnosticsService.SetAction($"Hover Preview Cooldown waiting: '{entry.Title}'");
                try { await Task.Delay(wait, token); }
                catch (OperationCanceledException) { return; }
            }

            // Re-check session after cooldown
            if (token.IsCancellationRequested) return;
            lock (_stateLock)
            {
                if (_activeSessionId != sessionId) return;
            }

            if (DebugFlags.SafeDebugMode)
            {
                // Create a beautiful, 100% safe mock preview drawing (no MediaPlayer!)
                var visual = new DrawingVisual();
                using (var ctx = visual.RenderOpen())
                {
                    var rect = new Rect(0, 0, 320, 180);
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 10, 11, 16)), null, rect);
                    
                    var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(102, 252, 241)), 1.5);
                    ctx.DrawRoundedRectangle(null, borderPen, rect, 4, 4);
                    
                    #pragma warning disable CS0618
                    var formattedText = new FormattedText(
                        "DEMO PLAYING",
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        11,
                        new SolidColorBrush(Color.FromRgb(102, 252, 241))
                    );
                    #pragma warning restore CS0618
                    ctx.DrawText(formattedText, new Point(115, 80));
                }

                var mockImage = new DrawingImage(visual.Drawing);
                entry.PreviewSource = mockImage;
                entry.IsPreviewActive = true;
                
                DiagnosticsService.OnPreviewStarted();
                DiagnosticsService.SetAction("Hover Preview Safe Demo active (SafeDebugMode)");
                return;
            }

            DiagnosticsService.SetAction($"Hover Preview Ensuring MediaPlayer: '{entry.Title}'");

            // Ensure player exists
            EnsurePlayerCreated_UIThread();

            // Tear down any previous drawing reference (not the player itself)
            if (_videoDrawing != null)
            {
                _videoDrawing.Player = null;
                _videoDrawing = null;
            }

            // Create fresh drawing and image for this session
            _videoDrawing = new VideoDrawing
            {
                Player = _mediaPlayer,
                Rect = new Rect(0, 0, 320, 180)
            };
            var drawingImage = new DrawingImage(_videoDrawing);

            // Create session-scoped TCS for startup watchdog
            var openedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sessionOpenedTcs = openedTcs;

            Debug.WriteLine($"{LogPrefix(sessionId)} Opening '{entry.Video}'");
            DiagnosticsService.SetAction($"Hover Preview MediaPlayer Opening File: '{entry.Video}'");
            
            _mediaPlayer!.Open(new Uri(entry.Video, UriKind.Absolute));

            // ────────────────────────────────────────────────────────────
            // 4-second startup watchdog.
            // CRITICAL: Lock is NOT held here. We await freely.
            // ────────────────────────────────────────────────────────────
            var watchdogDelay = Task.Delay(WatchdogStartupMs, token);
            Task completed;
            try
            {
                completed = await Task.WhenAny(openedTcs.Task, watchdogDelay);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"{LogPrefix(sessionId)} Watchdog await cancelled.");
                TearDownPlayer_UIThread();
                return;
            }

            // Final session guard after async gap
            if (token.IsCancellationRequested) { TearDownPlayer_UIThread(); return; }
            lock (_stateLock)
            {
                if (_activeSessionId != sessionId) { TearDownPlayer_UIThread(); return; }
            }

            if (completed == watchdogDelay)
            {
                Debug.WriteLine($"{LogPrefix(sessionId)} Startup watchdog TIMEOUT for '{entry.Title}'.");
                TearDownPlayer_UIThread();
                return;
            }

            bool opened = await openedTcs.Task;
            if (!opened)
            {
                Debug.WriteLine($"{LogPrefix(sessionId)} MediaFailed for '{entry.Title}'.");
                TearDownPlayer_UIThread();
                return;
            }

            DiagnosticsService.SetAction($"Hover Preview MediaPlayer Playing: '{entry.Title}'");

            // Success - start playback and activate visual state
            _mediaPlayer.Play();
            entry.PreviewSource = drawingImage;
            entry.IsPreviewActive = true;
            _lastResetTime = DateTime.UtcNow;

            DiagnosticsService.OnPreviewStarted(); // Track active preview sessions

            Debug.WriteLine($"{LogPrefix(sessionId)} Preview started for '{entry.Title}'.");

            // Start 8-second inactivity watchdog
            StartInactivityWatchdog(entry, sessionId, token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{LogPrefix(sessionId)} InitPlayer exception: {ex.Message}");
            TearDownPlayer_UIThread();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Teardown - always on UI thread, synchronous, no awaiting
    // ─────────────────────────────────────────────────────────────────────
    private void TearDownPlayer_UIThread()
    {
        DiagnosticsService.SetAction("Hover Preview Entering Teardown");
        _lastPreviewStopTime = DateTime.UtcNow;

        // Cancel watchdog
        CancellationTokenSource? wdCts;
        lock (_stateLock)
        {
            wdCts = _watchdogCts;
            _watchdogCts = null;
        }
        wdCts?.Cancel();
        wdCts?.Dispose();

        // Null the session TCS so stale MediaOpened callbacks are ignored
        _sessionOpenedTcs?.TrySetResult(false);
        _sessionOpenedTcs = null;

        // Clear visual state on previous entry
        WallpaperEntry? prevEntry;
        lock (_stateLock)
        {
            prevEntry = _activeEntry;
            _activeEntry = null;
        }
        if (prevEntry != null)
        {
            prevEntry.PreviewSource = null;
            prevEntry.IsPreviewActive = false;
        }

        // Pause player (keep singleton alive, avoid Close() which blocks message pump)
        if (_mediaPlayer != null)
        {
            try
            {
                DiagnosticsService.SetAction("Hover Preview Pausing MediaPlayer");
                _mediaPlayer.Pause();
                DiagnosticsService.SetAction("Hover Preview Resetting MediaPlayer Position");
                _mediaPlayer.Position = TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preview] Pause exception: {ex.Message}");
            }
        }

        // Detach drawing reference
        if (_videoDrawing != null)
        {
            _videoDrawing.Player = null;
            _videoDrawing = null;
        }

        _lastResetTime = DateTime.MinValue;
        DiagnosticsService.OnPreviewStopped(); // Track active preview sessions
        DiagnosticsService.SetAction("Hover Preview Idle / TearDown complete");
        Debug.WriteLine("[Preview] TearDown complete.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 8-Second inactivity watchdog (pure background, no locks held)
    // ─────────────────────────────────────────────────────────────────────
    private void StartInactivityWatchdog(WallpaperEntry entry, long sessionId, CancellationToken sessionToken)
    {
        CancellationTokenSource wdCts;
        lock (_stateLock)
        {
            _watchdogCts?.Cancel();
            _watchdogCts?.Dispose();
            _watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            wdCts = _watchdogCts;
        }

        var wdToken = wdCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                int idleMs = 0;
                while (idleMs < WatchdogInactivityMs && !wdToken.IsCancellationRequested)
                {
                    await Task.Delay(100, wdToken);
                    var sinceReset = (DateTime.UtcNow - _lastResetTime).TotalMilliseconds;
                    if (sinceReset < 150) idleMs = 0;
                    else idleMs += 100;
                }

                if (!wdToken.IsCancellationRequested)
                {
                    Debug.WriteLine($"{LogPrefix(sessionId)} 8s inactivity timeout - stopping preview.");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Only stop if this session is still the active one
                        lock (_stateLock)
                        {
                            if (_activeSessionId != sessionId) return;
                        }
                        TearDownPlayer_UIThread();
                        // Also kill the session CTS
                        CancellationTokenSource? cts;
                        lock (_stateLock)
                        {
                            cts = _activeCts;
                            _activeCts = null;
                        }
                        cts?.Cancel();
                        cts?.Dispose();
                    }, DispatcherPriority.Background);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"{LogPrefix(sessionId)} Inactivity watchdog cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LogPrefix(sessionId)} Watchdog error: {ex.Message}");
            }
        }, wdToken);
    }
}
