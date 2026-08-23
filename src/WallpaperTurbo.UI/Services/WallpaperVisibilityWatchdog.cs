// WallpaperVisibilityWatchdog.cs
//
// Detects whether the AppRunner's render window is ACTUALLY visible on the
// desktop (visible + attached to Progman/WorkerW + covering its monitor),
// independent of IPC ping or process existence. Pure observer: recovery
// policy (auto-restart, attempts, exhaustion) lives in MainViewModel.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WallpaperTurbo.UI.Services;

/// <summary>Screen-space rectangle (left/top inclusive, right/bottom exclusive).</summary>
public readonly record struct WindowRectInfo(int Left, int Top, int Right, int Bottom);

/// <summary>
/// A single AppRunner render window observed during a poll, with enough
/// information for the visibility decision. Pure data — populated either by
/// the real Win32 enumerator or by tests.
/// </summary>
public sealed record RenderWindowCandidate(
    IntPtr Hwnd,
    bool IsVisible,
    string? ParentClassName,
    WindowRectInfo WindowRect,
    IReadOnlyList<WindowRectInfo> MonitorRects);

/// <summary>Testable seam: produces render-window candidates for a poll.</summary>
internal interface IWindowEnumerationSource
{
    IReadOnlyList<RenderWindowCandidate> GetCandidates();
}

/// <summary>
/// Monitors the desktop for the wallpaper render window. Raises
/// <see cref="WallpaperLost"/> once per visible→hidden transition while the
/// engine is expected to be running, and <see cref="VisibilityChanged"/> on
/// every visibility transition. Events are marshaled to the UI dispatcher
/// when one exists (tests receive them synchronously).
/// </summary>
public interface IWallpaperVisibilityMonitor
{
    bool IsWallpaperVisible { get; }

    /// <summary>Raised when the wallpaper was visible and is no longer (engine expected).</summary>
    event EventHandler? WallpaperLost;

    /// <summary>Raised on every visible/hidden transition (bool = visible).</summary>
    event EventHandler<bool>? VisibilityChanged;

    /// <summary>Informs the watchdog whether the engine is supposed to be displaying a wallpaper.</summary>
    void SetEngineExpected(bool expected);

    /// <summary>Whether the watchdog currently expects the engine to be displaying a wallpaper.</summary>
    bool IsEngineExpected { get; }

    /// <summary>Polls until the wallpaper is on screen or the timeout elapses.</summary>
    Task<bool> WaitForVisibleAsync(TimeSpan timeout, CancellationToken ct = default);

    void Start();

    void Stop();
}

public sealed class WallpaperVisibilityWatchdog : IWallpaperVisibilityMonitor, IDisposable
{
    internal const string RenderWindowClassPrefix = "WallpaperTurbo_RenderWindow_Class";

    internal static readonly IReadOnlySet<string> DesktopParentClasses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Progman", "WorkerW" };

    internal const double MinMonitorCoverage = 0.9;

    /// <summary>
    /// Number of consecutive "not visible" polls (while the engine is expected to
    /// be running) required before <see cref="WallpaperLost"/> is raised. Debouncing
    /// prevents a relaunch storm during transient gaps such as the AppRunner's own
    /// Explorer-restart window recreation (~2s) or a momentary perf-pause.
    /// </summary>
    internal const int LostPollThreshold = 3;

    private readonly IWindowEnumerationSource _source;
    private readonly int _pollIntervalMs;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _isWallpaperVisible;
    private bool _engineExpected;
    private int _consecutiveNotVisible;
    // Whether the wallpaper has been observed ON SCREEN at least once since the engine
    // became expected. "Lost" is only raised after a real visible->hidden transition,
    // never during the engine's initial bring-up (AppRunner spends several seconds
    // recreating the WorkerW shell + render window before the window appears).
    private bool _hadVisibleWhileExpected;

    public bool IsWallpaperVisible
    {
        get
        {
            lock (_lock)
            {
                return _isWallpaperVisible;
            }
        }
    }

    public bool IsEngineExpected
    {
        get
        {
            lock (_lock)
            {
                return _engineExpected;
            }
        }
    }

    public event EventHandler? WallpaperLost;

    public event EventHandler<bool>? VisibilityChanged;

    /// <summary>DI constructor — real Win32 enumeration.</summary>
    public WallpaperVisibilityWatchdog()
        : this(new Win32WindowEnumerator())
    {
    }

    internal WallpaperVisibilityWatchdog(IWindowEnumerationSource source, int pollIntervalMs = 1000)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pollIntervalMs = pollIntervalMs;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_cts != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoop(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _cts;
            _cts = null;
        }

        cts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best effort — the loop is a background task.
        }
    }

    public void Dispose() => Stop();

    public void SetEngineExpected(bool expected)
    {
        lock (_lock)
        {
            _engineExpected = expected;
            // (Re)arm fresh: forget any prior "seen visible" state and missed-poll count
            // so we never fire a stale loss for a window that has not appeared yet.
            _consecutiveNotVisible = 0;
            _hadVisibleWhileExpected = false;
        }
    }

    /// <summary>
    /// Single poll cycle. Internal so tests can drive the state machine
    /// deterministically; the background loop just calls this repeatedly.
    /// </summary>
    internal void PollOnce()
    {
        bool visible = false;
        try
        {
            visible = IsOnScreen(_source.GetCandidates());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WallpaperVisibilityWatchdog] Poll failed: {ex.Message}");
        }

        bool lostTransition = false;
        bool changed = false;
        lock (_lock)
        {
            bool wasVisible = _isWallpaperVisible;
            if (visible)
            {
                _consecutiveNotVisible = 0;
                // Once we have actually seen the wallpaper on screen while the engine is
                // expected, we are permitted to report a later loss. This stops "lost"
                // firing during the engine's initial bring-up (AppRunner recreates the
                // WorkerW shell + render window over several seconds before the window is
                // visible), which previously triggered a premature relaunch/crash.
                if (_engineExpected)
                {
                    _hadVisibleWhileExpected = true;
                }
            }
            else
            {
                _consecutiveNotVisible++;
            }

            changed = visible != wasVisible;
            _isWallpaperVisible = visible;

            // Only treat the wallpaper as genuinely lost after it has been observed on
            // screen at least once AND has now been absent for several consecutive polls.
            // The "observed once" gate prevents a relaunch storm during the engine's
            // startup window; the consecutive-poll debounce prevents transient-gap
            // false positives (e.g. AppRunner's own Explorer-restart window recreation).
            if (_engineExpected && _hadVisibleWhileExpected && !visible && _consecutiveNotVisible >= LostPollThreshold)
            {
                lostTransition = true;
            }
        }

        if (lostTransition)
        {
            Debug.WriteLine("[WallpaperVisibilityWatchdog] WallpaperLost raised (engine expected, was visible, absent >= threshold).");
        }

        if (changed)
        {
            RaiseMarshaled(VisibilityChanged, visible);
        }

        if (lostTransition)
        {
            RaiseWallpaperLostMarshaled();
        }
    }

    public async Task<bool> WaitForVisibleAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        StartupDiagnostics.LogWithMemory($"WallpaperVisibilityWatchdog WaitForVisible START timeout={timeout.TotalSeconds}s");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested)
            {
                StartupDiagnostics.LogWithMemory($"WallpaperVisibilityWatchdog WaitForVisible CANCELLED after {sw.ElapsedMilliseconds}ms");
                return false;
            }

            try
            {
                // Enumerate OFF the calling (UI) thread: EnumWindows over every
                // top-level window and its children is expensive and must never block
                // the dispatcher. The enumeration is MTA-safe (it uses EnumDisplayMonitors
                // + plain user32 calls, not Screen.AllScreens) so Task.Run is fine.
                if (await Task.Run(() => IsOnScreen(_source.GetCandidates())).ConfigureAwait(false))
                {
                    StartupDiagnostics.LogWithMemory($"WallpaperVisibilityWatchdog WaitForVisible SUCCESS in {sw.ElapsedMilliseconds}ms");
                    return true;
                }
            }
            catch
            {
                // Keep polling until the timeout.
            }

            try
            {
                await Task.Delay(250, ct);
            }
            catch (OperationCanceledException)
            {
                StartupDiagnostics.LogWithMemory($"WallpaperVisibilityWatchdog WaitForVisible CANCELLED after {sw.ElapsedMilliseconds}ms");
                return false;
            }
        }

        StartupDiagnostics.LogWithMemory($"WallpaperVisibilityWatchdog WaitForVisible TIMEOUT after {sw.ElapsedMilliseconds}ms (budget={timeout.TotalSeconds}s)");
        return false;
    }

    /// <summary>
    /// Pure decision: any candidate that is visible, parented to the desktop
    /// shell (Progman/WorkerW), and covering ≥90% of any monitor counts as
    /// "wallpaper on screen".
    /// </summary>
    internal static bool IsOnScreen(IReadOnlyList<RenderWindowCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!candidate.IsVisible)
            {
                continue;
            }

            if (string.IsNullOrEmpty(candidate.ParentClassName) ||
                !DesktopParentClasses.Contains(candidate.ParentClassName))
            {
                continue;
            }

            foreach (var monitor in candidate.MonitorRects)
            {
                long coveredWidth = Math.Min(candidate.WindowRect.Right, monitor.Right)
                                    - Math.Max(candidate.WindowRect.Left, monitor.Left);
                long coveredHeight = Math.Min(candidate.WindowRect.Bottom, monitor.Bottom)
                                     - Math.Max(candidate.WindowRect.Top, monitor.Top);
                if (coveredWidth <= 0 || coveredHeight <= 0)
                {
                    continue;
                }

                long coveredArea = coveredWidth * coveredHeight;
                long monitorArea = (long)(monitor.Right - monitor.Left) * (monitor.Bottom - monitor.Top);
                if (monitorArea > 0 && (double)coveredArea / monitorArea >= MinMonitorCoverage)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PollOnce();
            try
            {
                Task.Delay(_pollIntervalMs, ct).Wait();
            }
            catch
            {
                break;
            }
        }
    }

    private static void RaiseMarshaled<T>(EventHandler<T>? handler, T args)
    {
        if (handler == null)
        {
            return;
        }

        void Invoke()
        {
            try
            {
                handler(null, args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WallpaperVisibilityWatchdog] VisibilityChanged handler threw: {ex}");
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Invoke();
            return;
        }

        dispatcher.BeginInvoke(Invoke, DispatcherPriority.Background);
    }

    private void RaiseWallpaperLostMarshaled()
    {
        var handler = WallpaperLost;
        if (handler == null)
        {
            return;
        }

        void Invoke()
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WallpaperVisibilityWatchdog] WallpaperLost handler threw: {ex}");
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Invoke();
            return;
        }

        dispatcher.BeginInvoke(Invoke, DispatcherPriority.Background);
    }
}

/// <summary>Real Win32 implementation: enumerates top-level + child windows by class prefix.</summary>
internal sealed class Win32WindowEnumerator : IWindowEnumerationSource
{
    public IReadOnlyList<RenderWindowCandidate> GetCandidates()
    {
        // Use EnumDisplayMonitors directly (not Screen.AllScreens) so this works on
        // any thread apartment. Screen.AllScreens throws on MTA threads (the watchdog's
        // poll loop runs on a Task.Run pool thread), which previously made detection
        // silently fail on the background thread.
        var monitorRects = GetMonitorRects();

        if (monitorRects.Count == 0)
        {
            return Array.Empty<RenderWindowCandidate>();
        }

        var candidates = new List<RenderWindowCandidate>();
        try
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                CollectRenderWindows(hwnd, monitorRects, candidates);
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Window enumeration failed — report nothing visible this poll.
        }

        return candidates;
    }

    private static void CollectRenderWindows(
        IntPtr hwnd,
        IReadOnlyList<WindowRectInfo> monitorRects,
        List<RenderWindowCandidate> candidates)
    {
        if (IsRenderWindowClass(hwnd))
        {
            candidates.Add(BuildCandidate(hwnd, monitorRects));
        }

        try
        {
            NativeMethods.EnumChildWindows(hwnd, (child, _) =>
            {
                if (IsRenderWindowClass(child))
                {
                    candidates.Add(BuildCandidate(child, monitorRects));
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Ignore per-window child enumeration failures.
        }
    }

    private static bool IsRenderWindowClass(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            return NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0 &&
                   sb.ToString().StartsWith(WallpaperVisibilityWatchdog.RenderWindowClassPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static RenderWindowCandidate BuildCandidate(IntPtr hwnd, IReadOnlyList<WindowRectInfo> monitorRects)
    {
        bool visible = NativeMethods.IsWindowVisible(hwnd);

        string? parentClass = null;
        IntPtr parent = NativeMethods.GetParent(hwnd);
        if (parent != IntPtr.Zero)
        {
            try
            {
                var sb = new System.Text.StringBuilder(256);
                if (NativeMethods.GetClassName(parent, sb, sb.Capacity) > 0)
                {
                    parentClass = sb.ToString();
                }
            }
            catch
            {
                parentClass = null;
            }
        }

        var rect = new WindowRectInfo(0, 0, 0, 0);
        if (NativeMethods.GetWindowRect(hwnd, out var rawRect))
        {
            rect = new WindowRectInfo(rawRect.Left, rawRect.Top, rawRect.Right, rawRect.Bottom);
        }

        return new RenderWindowCandidate(hwnd, visible, parentClass, rect, monitorRects);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public delegate bool MonitorEnumProc(
            IntPtr hMonitor,
            IntPtr hdcMonitor,
            ref RECT lprcMonitor,
            IntPtr dwData);
    }

    /// <summary>
    /// MTA-safe monitor-rect query. EnumDisplayMonitors is a plain user32 call that
    /// does not require an STA thread (unlike System.Windows.Forms.Screen.AllScreens),
    /// so it can be called from the watchdog's background poll loop and from
    /// Task.Run without throwing.
    /// </summary>
    private static List<WindowRectInfo> GetMonitorRects()
    {
        var rects = new List<WindowRectInfo>();
        try
        {
            NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr lpData) =>
                {
                    rects.Add(new WindowRectInfo(rc.Left, rc.Top, rc.Right, rc.Bottom));
                    return true;
                },
                IntPtr.Zero);
        }
        catch
        {
            // No display info available — caller treats an empty list as "cannot judge".
        }

        return rects;
    }
}
