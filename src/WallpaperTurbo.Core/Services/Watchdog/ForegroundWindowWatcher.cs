using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Services.Performance;

namespace WallpaperTurbo.Core.Services.Watchdog;

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
    private PauseMode _pauseMode = PauseMode.Maximized;

    public PauseMode PauseMode
    {
        get => _pauseMode;
        set
        {
            if (_pauseMode != value)
            {
                _pauseMode = value;
                if (value == PauseMode.None && _lastState)
                {
                    _lastState = false;
                    VisibilityChanged?.Invoke(false);
                }
            }
        }
    }

    /// <summary>
    /// Process ID of the managing UI application. Windows owned by this PID are
    /// never treated as obscuring the desktop, so the wallpaper keeps playing
    /// while the WallpaperTurbo UI is in the foreground.
    /// </summary>
    public int ExcludedPid { get; set; } = 0;

    public ForegroundWindowWatcher(PauseMode mode = PauseMode.Maximized)
    {
        _pauseMode = mode;
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
                if (PauseMode != PauseMode.None)
                {
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

        // Verify the window is actually visible on screen.
        // Invisible/ghost foreground windows should never pause the wallpaper.
        if (!IsWindowVisible(hwnd))
        {
            return false;
        }

        // Check if the window is cloaked (e.g. UWP suspended windows or virtual desktop transitions)
        // DWMWA_CLOAKED = 14.
        if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        // Check if the window is a transparent click-through overlay (e.g. FPS counters, screen recorders).
        // GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x00000020.
        int exStyle = GetWindowLong(hwnd, -20);
        if ((exStyle & 0x00000020) != 0)
        {
            return false;
        }

        if (PauseMode == PauseMode.None)
        {
            return false;
        }

        if (PauseMode == PauseMode.Focused)
        {
            // Verify class name of foreground window to avoid pausing when on desktop, taskbar or widgets
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string name = className.ToString();

            if (DesktopClasses.Contains(name))
            {
                return false; // User is on the desktop background, icons, taskbar, start menu, or widgets
            }
            return true; // Any non-desktop application focused -> Pause!
        }

        // Default: PauseMode.Maximized (current behavior)
        // Get the monitor containing the foreground window to check for obscuring windows on it
        IntPtr hMonitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        if (hMonitor != IntPtr.Zero)
        {
            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                RECT monRect = monitorInfo.rcMonitor;
                RECT workRect = monitorInfo.rcWork;
                int monitorWidth = monRect.Right - monRect.Left;
                int monitorHeight = monRect.Bottom - monRect.Top;
                long monitorArea = (long)monitorWidth * monitorHeight;
                long workArea = (long)(workRect.Right - workRect.Left) * (workRect.Bottom - workRect.Top);

                uint currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

                // 1. FAST PATH: Check if the foreground window itself is maximized/fullscreen.
                // Exclude desktop, taskbar, start menu, and our own processes.
                StringBuilder className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                string fgClassName = className.ToString();

                if (!DesktopClasses.Contains(fgClassName))
                {
                    GetWindowThreadProcessId(hwnd, out uint fgOwnerPid);
                    if (fgOwnerPid != currentPid && (ExcludedPid <= 0 || fgOwnerPid != (uint)ExcludedPid))
                    {
                        // Native Win32 maximized check
                        if (IsZoomed(hwnd))
                        {
                            return true;
                        }

                        if (GetWindowRect(hwnd, out RECT rect))
                        {
                            int w = rect.Right - rect.Left;
                            int h = rect.Bottom - rect.Top;

                            // Fullscreen check on this monitor
                            if (rect.Left <= monRect.Left && rect.Top <= monRect.Top &&
                                rect.Right >= monRect.Right && rect.Bottom >= monRect.Bottom)
                            {
                                return true;
                            }

                            // Work area maximized check
                            if (rect.Left <= workRect.Left && rect.Top <= workRect.Top &&
                                rect.Right >= workRect.Right && rect.Bottom >= workRect.Bottom)
                            {
                                return true;
                            }

                            // Maximized area check (>= 85% of work area or monitor area)
                            long windowArea = (long)w * h;
                            if ((workArea > 0 && windowArea >= workArea * 0.85) || windowArea >= monitorArea * 0.85)
                            {
                                return true;
                            }
                        }
                    }
                }

                // 2. SLOW PATH: Walk top-level windows in Z-order.
                // Stop when we reach a window owned by our player process (currentPid) on this monitor
                // because any window below the wallpaper cannot obscure it.
                IntPtr wnd = GetTopWindow(IntPtr.Zero);
                while (wnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(wnd, out uint ownerPid);
                    if (ownerPid == currentPid)
                    {
                        // Skip our own process windows, but do NOT break the loop since we might have hidden utility windows at the top of Z-order.
                        wnd = GetWindow(wnd, GW_HWNDNEXT);
                        continue;
                    }

                    // Only check visible, non-minimized windows intersecting our target monitor
                    if (IsWindowVisible(wnd) && !IsIconic(wnd) && MonitorFromWindow(wnd, 0) == hMonitor)
                    {
                        // Exclude desktop and shell windows
                        StringBuilder wndClassName = new StringBuilder(256);
                        GetClassName(wnd, wndClassName, wndClassName.Capacity);
                        string wndName = wndClassName.ToString();

                        if (!DesktopClasses.Contains(wndName))
                        {
                            // Exclude transparent overlays
                            int wndExStyle = GetWindowLong(wnd, -20);
                            bool transparent = (wndExStyle & 0x00000020) != 0;
                            int wndCloaked = 0;
                            DwmGetWindowAttribute(wnd, 14, out wndCloaked, sizeof(int));

                            if (!transparent && wndCloaked == 0)
                            {
                                // Exclude UI process (ExcludedPid)
                                if (ownerPid != currentPid && (ExcludedPid <= 0 || ownerPid != (uint)ExcludedPid))
                                {
                                    if (IsZoomed(wnd))
                                    {
                                        return true;
                                    }

                                    if (GetWindowRect(wnd, out RECT rect))
                                    {
                                        int w = rect.Right - rect.Left;
                                        int h = rect.Bottom - rect.Top;
                                        long windowArea = (long)w * h;

                                        // Fullscreen check on this monitor
                                        if (rect.Left <= monRect.Left && rect.Top <= monRect.Top &&
                                            rect.Right >= monRect.Right && rect.Bottom >= monRect.Bottom)
                                        {
                                            return true;
                                        }

                                        // Work area check on this monitor
                                        if (rect.Left <= workRect.Left && rect.Top <= workRect.Top &&
                                            rect.Right >= workRect.Right && rect.Bottom >= workRect.Bottom)
                                        {
                                            return true;
                                        }

                                        // Maximized check on this monitor (>= 85% area)
                                        if ((workArea > 0 && windowArea >= workArea * 0.85) || windowArea >= monitorArea * 0.85)
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    wnd = GetWindow(wnd, GW_HWNDNEXT);
                }
                return false;
            }
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

    private static int GetWindowLong(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
            return (int)GetWindowLongPtr64(hWnd, nIndex);
        else
            return GetWindowLong32(hWnd, nIndex);
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

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const uint GW_HWNDNEXT = 2;
}
