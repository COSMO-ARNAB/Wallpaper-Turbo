using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperTurbo.Core.Interop;

public static class NativeMethods
{
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public static readonly IntPtr HWND_BOTTOM =
        new(1);

    [Flags]
    public enum WindowStyles : uint
    {
        WS_POPUP = 0x80000000,
        WS_CHILD = 0x40000000,
        WS_VISIBLE = 0x10000000,

        WS_EX_LAYERED = 0x00080000,
        WS_EX_TOOLWINDOW = 0x00000080,
        WS_EX_NOACTIVATE = 0x08000000,
        WS_EX_APPWINDOW = 0x00040000,
        WS_EX_NOREDIRECTIONBITMAP = 0x00200000,

        WS_CLIPSIBLINGS = 0x04000000,
        WS_CLIPCHILDREN = 0x02000000,
    }

    [Flags]
    public enum SetWindowPosFlags : uint
    {
        SWP_NOSIZE = 0x0001,
        SWP_NOMOVE = 0x0002,
        SWP_NOZORDER = 0x0004,
        SWP_NOREDRAW = 0x0008,
        SWP_NOACTIVATE = 0x0010,
        SWP_FRAMECHANGED = 0x0020,
        SWP_SHOWWINDOW = 0x0040,
        SWP_HIDEWINDOW = 0x0080,
        SWP_NOCOPYBITS = 0x0100,
        SWP_NOOWNERZORDER = 0x0200,
        SWP_NOSENDCHANGING = 0x0400
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public delegate bool EnumWindowsProc(
        IntPtr hwnd,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(
        string lpClassName,
        string? lpWindowName);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(
        IntPtr parentHandle,
        IntPtr childAfter,
        string? className,
        string? windowTitle);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(
        EnumWindowsProc lpEnumFunc,
        IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(
        IntPtr hwndParent,
        EnumWindowsProc lpEnumFunc,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    public static extern int GetClassName(
        IntPtr hWnd,
        StringBuilder lpClassName,
        int nMaxCount);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern IntPtr SetParent(
        IntPtr hWndChild,
        IntPtr hWndNewParent);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern IntPtr GetParent(
        IntPtr hWnd);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern bool IsWindowVisible(
        IntPtr hWnd);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern bool IsWindow(
        IntPtr hWnd);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern int GetWindowLong(
        IntPtr hWnd,
        int nIndex);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern int SetWindowLong(
        IntPtr hWnd,
        int nIndex,
        int dwNewLong);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern bool GetWindowRect(
        IntPtr hWnd,
        out RECT lpRect);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern int GetSystemMetrics(
        int nIndex);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd,
        uint crKey,
        byte bAlpha,
        uint dwFlags);

    [DllImport("user32.dll")]
    public static extern int MapWindowPoints(
        IntPtr hWndFrom,
        IntPtr hWndTo,
        ref RECT lpPoints,
        uint cPoints);
}