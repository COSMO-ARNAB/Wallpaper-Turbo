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
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    bool isObscured = CheckIfWindowObscuresScreen(fgHwnd);
                    if (isObscured != _lastState)
                    {
                        _lastState = isObscured;
                        VisibilityChanged?.Invoke(isObscured);
                    }
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

        int screenWidth = GetSystemMetrics(0); // SM_CXSCREEN
        int screenHeight = GetSystemMetrics(1); // SM_CYSCREEN

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;

        // Cover full screen dimensions (typical fullscreen games)
        if (rect.Left <= 0 && rect.Top <= 0 && rect.Right >= screenWidth && rect.Bottom >= screenHeight)
        {
            return true;
        }

        // Cover maximized states (takes up at least 95% of primary monitor bounds)
        long screenArea = (long)screenWidth * screenHeight;
        long windowArea = (long)w * h;
        if (windowArea >= screenArea * 0.95)
        {
            return true;
        }

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
}
