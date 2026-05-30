using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperTurbo.Core.Services.Stability;

/// <summary>
/// Monitors Windows Explorer restarts by listening to the system-wide "TaskbarCreated" message
/// broadcast using a dedicated, hidden top-level window running in an STA thread message loop.
/// </summary>
public sealed class ExplorerRestartMonitor : IDisposable
{
    private static readonly uint WM_TASKBARCREATED;
    private static readonly string ClassName = "WallpaperTurbo_ExplorerRestartMonitorClass";

    static ExplorerRestartMonitor()
    {
        // Register the system-wide TaskbarCreated message broadcast
        WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
    }

    public event Action? RestartDetected;
    public event Action? DisplaySettingsChanged;

    private const uint WM_DISPLAYCHANGE = 0x007E;

    private readonly IntPtr _hwnd;
    private readonly Thread _messageThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly WndProcDelegate _wndProcDelegate;
    private bool _disposed;

    public ExplorerRestartMonitor()
    {
        _wndProcDelegate = WndProc;
        var initSignal = new ManualResetEventSlim(false);
        IntPtr createdHwnd = IntPtr.Zero;

        _messageThread = new Thread(() =>
        {
            try
            {
                var wndClass = new WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = _wndProcDelegate,
                    hInstance = GetModuleHandle(null),
                    lpszClassName = ClassName
                };

                RegisterClassEx(ref wndClass);

                // Create a hidden, top-level window to receive broadcasts
                createdHwnd = CreateWindowEx(
                    0,
                    ClassName,
                    "ExplorerRestartMonitorWindow",
                    0,
                    0, 0, 0, 0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    GetModuleHandle(null),
                    IntPtr.Zero
                );

                initSignal.Set();

                if (createdHwnd == IntPtr.Zero)
                    return;

                // Message Loop
                while (!_cts.Token.IsCancellationRequested)
                {
                    if (GetMessage(out var msg, IntPtr.Zero, 0, 0))
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch
            {
                initSignal.Set();
            }
            finally
            {
                if (createdHwnd != IntPtr.Zero)
                {
                    DestroyWindow(createdHwnd);
                }
                UnregisterClass(ClassName, GetModuleHandle(null));
            }
        });

        _messageThread.IsBackground = true;
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        initSignal.Wait();
        _hwnd = createdHwnd;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TASKBARCREATED)
        {
            // Fire event asynchronously to prevent blocking the message loop
            Task.Run(() => RestartDetected?.Invoke());
            return IntPtr.Zero;
        }
        else if (msg == WM_DISPLAYCHANGE)
        {
            // Invoke synchronously on the STA message thread to prevent out-of-order event storms
            DisplaySettingsChanged?.Invoke();
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT (0x0012) to exit GetMessage message loop
        }
        _messageThread.Join(2000);
        _cts.Dispose();
    }

    // Native Structures & Delegates
    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // DllImports
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
