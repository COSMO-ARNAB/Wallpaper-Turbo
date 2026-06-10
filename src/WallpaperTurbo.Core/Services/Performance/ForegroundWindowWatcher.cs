using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperTurbo.Core.Services.Performance;

/// <summary>
/// Monitors the active foreground window to detect if it is maximized or running in fullscreen mode.
/// Automatically triggers visibility change events to allow pausing rendering when the wallpaper is obscured.
/// </summary>
public sealed class ForegroundWindowWatcher : IDisposable
{
    // Fires true when obscured/fullscreen (needs pause), false when desktop is visible (needs play)
    public event Action<bool>? VisibilityChanged;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _watcherTask;
    private bool _lastState; // true = covered/paused, false = active/playing
    private bool _disposed;

    public PauseMode PauseMode { get; set; }

    /// <summary>
    /// Process ID of the managing UI application. Windows owned by this PID are
    /// never treated as obscuring the desktop, so the wallpaper keeps playing
    /// while the WallpaperTurbo UI is in the foreground.
    /// </summary>
    public int ExcludedPid { get; set; } = 0;

    public ForegroundWindowWatcher(PauseMode mode = PauseMode.Maximized)
    {
        PauseMode = mode;
        _watcherTask = Task.Run(() => WatchLoopAsync(_cts.Token));
    }

    private async Task WatchLoopAsync(CancellationToken token)
    {
        // Allow the system to settle during startup
        await Task.Delay(2000, token).ConfigureAwait(false);

        while (!token.IsCancellationRequested)
        {
            try
            {
                bool isObscured = false;
                if (IsSessionLocked())
                {
                    isObscured = true;
                }
                else
                {
                    IntPtr fgHwnd = GetForegroundWindow();
                    if (fgHwnd != IntPtr.Zero)
                    {
                        isObscured = CheckIfWindowObscuresScreen(fgHwnd);
                    }
                    else
                    {
                        // Maintain the last state during transient focus losses (e.g. alt-tabbing)
                        // to avoid rapid toggle/flicker.
                        isObscured = _lastState;
                    }
                }

                if (isObscured != _lastState)
                {
                    _lastState = isObscured;
                    VisibilityChanged?.Invoke(isObscured);
                }
            }
            catch
            {
                // Defensive catch to prevent loop termination on background thread
            }

            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> DesktopClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkerW",
        "Progman",
        "Windows.UI.Core.CoreWindow",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "WindowsDashboard",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "RainmeterMeterWindow",
        "_cls_desk_"
    };

    private bool CheckIfWindowObscuresScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        // Skip direct desktop or shell handles
        if (hwnd == GetDesktopWindow() || hwnd == GetShellWindow())
        {
            return false;
        }

        // Exclude the WallpaperTurbo UI process from triggering pause.
        // Without this, the management UI window (which can be maximized) would
        // immediately suspend the wallpaper every time the user interacts with it.
        if (ExcludedPid > 0)
        {
            GetWindowThreadProcessId(hwnd, out uint ownerPid);
            if (ownerPid == (uint)ExcludedPid)
                return false;
        }

        // Verify class name of foreground window to avoid pausing when on desktop, taskbar or widgets
        StringBuilder className = new StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        string name = className.ToString();

        if (DesktopClasses.Contains(name))
        {
            return false; // User is on the desktop background, icons, taskbar, start menu, or widgets
        }

        if (PauseMode == PauseMode.None)
        {
            return false;
        }

        if (PauseMode == PauseMode.Focused)
        {
            return true; // Any non-desktop application focused -> Pause!
        }

        // Default: PauseMode.Maximized (current behavior)
        if (!GetWindowRect(hwnd, out RECT rect))
            return false;

        // Use MonitorFromWindow to support multi-monitor setups correctly
        IntPtr hMonitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        if (hMonitor != IntPtr.Zero)
        {
            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                RECT monRect = monitorInfo.rcMonitor;
                int monitorWidth = monRect.Right - monRect.Left;
                int monitorHeight = monRect.Bottom - monRect.Top;

                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;

                // Cover full screen dimensions on that specific monitor (typical fullscreen games)
                if (rect.Left <= monRect.Left && rect.Top <= monRect.Top &&
                    rect.Right >= monRect.Right && rect.Bottom >= monRect.Bottom)
                {
                    return true;
                }

                // Cover maximized states on that monitor (takes up at least 95% of that monitor's bounds)
                long monitorArea = (long)monitorWidth * monitorHeight;
                long windowArea = (long)w * h;
                if (windowArea >= monitorArea * 0.95)
                {
                    return true;
                }

                return false;
            }
        }

        // Fallback to primary monitor logic if monitor APIs failed
        int screenWidth = GetSystemMetrics(0); // SM_CXSCREEN
        int screenHeight = GetSystemMetrics(1); // SM_CYSCREEN

        int wFallback = rect.Right - rect.Left;
        int hFallback = rect.Bottom - rect.Top;

        // Cover full screen dimensions (typical fullscreen games)
        if (rect.Left <= 0 && rect.Top <= 0 && rect.Right >= screenWidth && rect.Bottom >= screenHeight)
        {
            return true;
        }

        // Cover maximized states (takes up at least 95% of primary monitor bounds)
        long screenArea = (long)screenWidth * screenHeight;
        long windowAreaFallback = (long)wFallback * hFallback;
        if (windowAreaFallback >= screenArea * 0.95)
        {
            return true;
        }

        return false;
    }

    private bool IsSessionLocked()
    {
        IntPtr hDesk = OpenInputDesktop(0, false, 1); // 1 = DESKTOP_READOBJECTS
        if (hDesk == IntPtr.Zero)
        {
            // Returns zero if current desktop is not accessible (locked screen or secure UAC desktop)
            return true;
        }
        CloseDesktop(hDesk);
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try
        {
            _watcherTask.Wait(1000);
        }
        catch
        {
            // Suppress clean-up wait thread aborts
        }
        _cts.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);
}
