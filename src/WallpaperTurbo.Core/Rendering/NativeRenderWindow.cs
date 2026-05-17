// NativeRenderWindow.cs - Contains the implementation of a native Win32 window used as a rendering target for VLC. This allows us to render video directly to a window that can be embedded into the desktop background.
using System;
using System.Runtime.InteropServices;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering;

public static class NativeRenderWindow
{               

                private const int CS_VREDRAW = 0x0001;              
                private const int CS_HREDRAW = 0x0002;
                private const int WS_POPUP = unchecked((int)0x80000000);
                private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
                private const int WS_VISIBLE = 0x10000000;
                private const int SW_SHOW = 5;
                private const uint WM_DESTROY = 0x0002;
                private const uint WM_CLOSE = 0x0010;

                private static readonly string ClassName = "WallpaperTurbo_TestWindow_Class";

                public static Task<IntPtr> CreateAsync()
                {
                    var tcs = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);

                    var thread = new Thread(() =>
                    {
                        try
                        {
                            var hInstance = GetModuleHandle(null);

                            WNDCLASSEXW wnd = new WNDCLASSEXW();
                            wnd.cbSize = Marshal.SizeOf<WNDCLASSEXW>();
                            wnd.style = CS_HREDRAW | CS_VREDRAW;
                            wnd.lpfnWndProc = WndProc;
                            wnd.cbClsExtra = 0;
                            wnd.cbWndExtra = 0;
                            wnd.hInstance = hInstance;
                            wnd.hCursor = LoadCursor(IntPtr.Zero, (IntPtr)32512); // IDC_ARROW
                            wnd.hbrBackground = CreateSolidBrush(RGB(0, 0, 0)); // Black background
                            wnd.lpszClassName = ClassName;

                            var atom = RegisterClassExW(ref wnd);
                            if (atom == 0)
                            {
                                tcs.SetException(new InvalidOperationException("RegisterClassEx failed."));
                                return;
                            }

                            // Start at 0,0 and make it 1920x1080 (VLC will auto-scale, but good to have a base size)
                            var hwnd = CreateWindowExW(
                                0x08000000 | 0x00000080, // WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW (prevents showing in Alt-Tab)
                                ClassName,
                                "Wallpaper Turbo Video Canvas",
                                WS_POPUP | WS_VISIBLE,
                                0,
                                0,
                                NativeMethods.GetSystemMetrics(0),
                                NativeMethods.GetSystemMetrics(1),
                                IntPtr.Zero,
                                IntPtr.Zero,
                                hInstance,
                                IntPtr.Zero);

                            if (hwnd == IntPtr.Zero)
                            {
                                UnregisterClassW(ClassName, hInstance);
                                tcs.SetException(new InvalidOperationException("CreateWindowEx failed."));
                                return;
                            }

                            ShowWindow(hwnd, SW_SHOW);
                            UpdateWindow(hwnd);
                            NativeMethods.SetWindowPos(
                                hwnd,
                                new IntPtr(1), // HWND_BOTTOM
                                0,
                                0,
                                0,
                                0,
                                0x0002 | 0x0001 | 0x0010 // NOSIZE | NOMOVE | NOACTIVATE
                            );

                            tcs.SetResult(hwnd);

                            // Standard message loop for this thread.
                            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) != 0)
                            {
                                TranslateMessage(ref msg);
                                DispatchMessage(ref msg);
                            }

                            // Clean up class when done.
                            UnregisterClassW(ClassName, hInstance);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }) { IsBackground = true };

                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();

                    return tcs.Task;
                }

                public static void Shutdown(IntPtr hwnd)
                {
                    if (hwnd == IntPtr.Zero) return;
                    PostMessage(hwnd, WM_CLOSE, UIntPtr.Zero, IntPtr.Zero);
                }

                private static IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
                {
                    switch (msg)
                    {
                        case WM_CLOSE:
                            DestroyWindow(hWnd);
                            return IntPtr.Zero;
                        case WM_DESTROY:
                            PostQuitMessage(0);
                            return IntPtr.Zero;
                    }

                    return DefWindowProcW(hWnd, msg, wParam, lParam);
                }

                #region Native declarations

                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
                private struct WNDCLASSEXW
                {
                    public int cbSize;
                    public int style;
                    [MarshalAs(UnmanagedType.FunctionPtr)]
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
                    public UIntPtr wParam;
                    public IntPtr lParam;
                    public uint time;
                    public POINT pt;
                    public uint lPrivate;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct POINT
                {
                    public int x;
                    public int y;
                }

                private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

                [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                private static extern ushort RegisterClassExW([In] ref WNDCLASSEXW lpwcx);

                [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                private static extern IntPtr CreateWindowExW(
                    int dwExStyle,
                    [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
                    [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
                    int dwStyle,
                    int x,
                    int y,
                    int nWidth,
                    int nHeight,
                    IntPtr hWndParent,
                    IntPtr hMenu,
                    IntPtr hInstance,
                    IntPtr lpParam);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern bool UpdateWindow(IntPtr hWnd);

                [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                private static extern bool UnregisterClassW([MarshalAs(UnmanagedType.LPWStr)] string lpClassName, IntPtr hInstance);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern bool DestroyWindow(IntPtr hWnd);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

                [DllImport("user32.dll")] 
                private static extern bool TranslateMessage([In] ref MSG lpMsg);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

                [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

                [DllImport("gdi32.dll", SetLastError = true)]
                private static extern IntPtr CreateSolidBrush(uint crColor);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern bool PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern void PostQuitMessage(int nExitCode);

                private static uint RGB(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

                #endregion
 }




      