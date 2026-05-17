// WallpaperManager.cs - Manages the interaction with the Windows desktop shell to enable rendering behind icons.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WallpaperTurbo.Core.Interop;
using WallpaperTurbo.Core.Wallpaper;
//using WallpaperTurbo.Core.Media;

namespace WallpaperTurbo.Core.Wallpaper;

    /// <summary>
    /// Manages attaching windows into the desktop background (WorkerW) so content can render behind icons.
    /// </summary>
    public interface IWallpaperManager
    {
        /// <summary>
        /// Locates and initializes the desktop "WorkerW" handle that sits behind the shell icons.
        /// </summary>
        void InitializeDesktopHandle();

        /// <summary>
        /// Attaches a window into the desktop background so it appears behind the icons.
        /// </summary>
        IntPtr WorkerWHandle { get; }
        
        /// <param name="childWindowHandle">Handle to the child window to reparent.</param>
        void AttachWindow(IntPtr childWindowHandle);
    }

    /// <summary>
    /// Windows implementation of <see cref="IWallpaperManager"/> using classic Win32 techniques.
    /// </summary>
    public sealed class WindowsWallpaperManager : IWallpaperManager
    {
        private readonly object _sync = new();
        private IntPtr _workerW = IntPtr.Zero;
        private readonly Action<string>? _log;
        
        public IntPtr WorkerWHandle => _workerW;
        private const uint WM_SPAWN_WORKER = 0x052C;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint SENDMSG_TIMEOUT_MS = 1000;

        /// <summary>
        /// Creates a new instance. An optional structured logger may be provided; otherwise <see cref="Trace"/> is used.
        /// </summary>
        /// <param name="logger">Optional structured logger callback receiving a single string message.</param>
        public WindowsWallpaperManager(Action<string>? logger = null)
        {
            _log = logger;
        }

        /// <inheritdoc />
        public void InitializeDesktopHandle()
        {
            lock (_sync)
            {
                if (_workerW != IntPtr.Zero)
                {
                    Log("InitializeDesktopHandle", "WorkerW already initialized", ("workerW", PtrToString(_workerW)));
                    return;
                }

                Log("InitializeDesktopHandle", "Starting desktop handle initialization");

                var progman = NativeMethods.FindWindowW("Progman", null);
                Log("FindWindowW", "Progman lookup completed", ("progman", PtrToString(progman)));

                try
                {
                    // Request the OS to split the desktop shell layers
                    UIntPtr result;
                    var sendResult = NativeMethods.SendMessageTimeout(progman, WM_SPAWN_WORKER, UIntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, SENDMSG_TIMEOUT_MS, out result);
                    Log("SendMessageTimeout", "Requested Progman to spawn WorkerW", ("return", PtrToString(sendResult)), ("result", result.ToString()));
                }
                catch (Exception ex)
                {
                    Log("SendMessageTimeout.Error", "SendMessageTimeout threw an exception", ("exception", ex.ToString()));
                }

                // 1. Identify which top-level window explicitly contains the desktop icon shell (SHELLDLL_DefView)
                IntPtr shellWindow = progman;
                IntPtr shellView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

                if (shellView == IntPtr.Zero)
                {
                    // If it's not under Progman, scan other top-level windows for the WorkerW window housing it
                    NativeMethods.EnumWindows((hwnd, lParam) =>
                    {
                        IntPtr view = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (view != IntPtr.Zero)
                        {
                            shellWindow = hwnd;
                            shellView = view;
                            return false; // Stop scanning, shell found
                        }
                        return true;
                    }, IntPtr.Zero);
                }

                Log("InitializeDesktopHandle", "Desktop shell view host identified", ("hostWindow", PtrToString(shellWindow)));

                // 2. The true wallpaper target is the WorkerW window positioned directly behind the shellWindow in Z-order
                IntPtr trueWallpaperWorker = NativeMethods.FindWindowEx(IntPtr.Zero, shellWindow, "WorkerW", null);

                // Emergency Fallback: If Z-order indexing returned null, isolate the first standalone background canvas
                if (trueWallpaperWorker == IntPtr.Zero)
                {
                    Log("InitializeDesktopHandle.Warning", "Z-order sibling lookup returned null; applying fallback search");
                    
                    NativeMethods.EnumWindows((hwnd, lParam) =>
                    {
                        var sb = new StringBuilder(256);
                        var len = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                        if (string.Equals(len > 0 ? sb.ToString() : string.Empty, "WorkerW", StringComparison.Ordinal))
                        {
                            if (NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                            {
                                trueWallpaperWorker = hwnd;
                                return false; // Lock the first standalone layer candidate
                            }
                        }
                        return true;
                    }, IntPtr.Zero);
                }

                if (trueWallpaperWorker == IntPtr.Zero)
                {
                    Log("InitializeDesktopHandle.Failed", "Unable to locate a valid WorkerW background canvas layer");
                    _workerW = IntPtr.Zero;
                }
                else
                {
                    _workerW = trueWallpaperWorker;
                    Log("InitializeDesktopHandle.Success", "Wallpaper canvas handle verified and locked", ("workerW", PtrToString(_workerW)));
                }
            }
        }

        /// <inheritdoc />
        public void AttachWindow(IntPtr childWindowHandle)
        {
            if (childWindowHandle == IntPtr.Zero)
                throw new ArgumentException("childWindowHandle must be a valid window handle", nameof(childWindowHandle));

            lock (_sync)
            {
                if (_workerW == IntPtr.Zero)
                {
                    Log("AttachWindow.Info", "WorkerW not initialized; initializing now");
                    InitializeDesktopHandle();
                }

                if (_workerW == IntPtr.Zero)
                {
                    Log("AttachWindow.Failed", "Cannot attach window because WorkerW was not located");
                    throw new InvalidOperationException("WorkerW handle is not initialized. Call InitializeDesktopHandle() first or ensure the shell is available.");
                }

                try
                {
                    var stylePtr = NativeMethods.GetWindowLongPtr(childWindowHandle, NativeMethods.GWL_STYLE);
                    long style = stylePtr.ToInt64();
                    ulong ustyle = (ulong)style;

                    ustyle &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME | NativeMethods.WS_SYSMENU);
                    ustyle |= NativeMethods.WS_CHILD;

                    var newStylePtr = new IntPtr((long)ustyle);
                    var setRes = NativeMethods.SetWindowLongPtr(childWindowHandle, NativeMethods.GWL_STYLE, newStylePtr);
                    if (setRes == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                    {
                        var err = Marshal.GetLastWin32Error();
                        Log("SetWindowLongPtr.Failed", "Failed to change window styles before parenting", ("child", PtrToString(childWindowHandle)), ("error", err.ToString()));
                        throw new InvalidOperationException($"SetWindowLongPtr failed with Win32 error code {err}.");
                    }

                    Log("SetWindowLongPtr.Success", "Adjusted child window styles for parenting", ("child", PtrToString(childWindowHandle)), ("newStyle", ustyle.ToString("X")));
                }
                catch (Exception ex)
                {
                    Log("PreParent.Error", "Failed while preparing child window for parenting", ("exception", ex.ToString()));
                    throw;
                }

                var previousParent = NativeMethods.SetParent(childWindowHandle, _workerW);

                if (previousParent == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                
                    Log(
                        "SetParent.Failed",
                        "SetParent returned zero; operation may have failed",
                        ("child", PtrToString(childWindowHandle)),
                        ("workerW", PtrToString(_workerW)),
                        ("error", err.ToString())
                    );
                
                    throw new InvalidOperationException(
                        $"SetParent failed with Win32 error code {err}."
                    );
                }
                
                Log(
                    "SetParent.Success",
                    "Attached child window to WorkerW",
                    ("child", PtrToString(childWindowHandle)),
                    ("workerW", PtrToString(_workerW)),
                    ("previousParent", PtrToString(previousParent))
                );
                try
                {
                    int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
                    int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
                    
                    bool sizingResult = NativeMethods.SetWindowPos(
                        childWindowHandle, 
                        IntPtr.Zero, 
                        0, 0, width, height, 
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW
                    );

                    if (!sizingResult)
                    {
                        var err = Marshal.GetLastWin32Error();
                        Log("SetWindowPos.Failed", "Failed to adjust sizing boundaries over desktop workspace layer", ("error", err.ToString()));
                    }
                    else
                    {
                        Log("SetWindowPos.Success", "Forced background viewport bounds alignment", ("width", width), ("height", height));
                    }
                }
                catch (Exception ex)
                {
                    Log("SetWindowPos.Error", "Exception thrown during desktop display matching bounds alignment", ("exception", ex.ToString()));
                }
            }
        }

        private void Log(string @event, string message, params (string Key, object? Value)[] fields)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"event\":\"{@event}\"");
            sb.Append($",\"message\":\"{EscapeJsonString(message)}\"");
            foreach (var (k, v) in fields)
            {
                sb.Append(',');
                sb.Append($"\"{EscapeJsonString(k)}\":\"{EscapeJsonString(v?.ToString() ?? string.Empty)}\"");
            }
            sb.Append('}');

            if (_log != null)
                _log(sb.ToString());
            else
                Trace.TraceInformation(sb.ToString());
        }

        private static string PtrToString(IntPtr p)
        {
            if (p == IntPtr.Zero) return "0x0";
            return IntPtr.Size == 8 ? $"0{p.ToInt64():X}" : $"0x{p.ToInt32():X}";
        }

        private static string EscapeJsonString(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") ?? string.Empty;
        }
    }
