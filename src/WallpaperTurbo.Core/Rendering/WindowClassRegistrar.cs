// WindowClassRegistrar.cs

using System;
using System.Runtime.InteropServices;

namespace WallpaperTurbo.Core.Rendering;

public static class WindowClassRegistrar
{
    private const int CS_VREDRAW =
        0x0001;

    private const int CS_HREDRAW =
        0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
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

    public delegate IntPtr WndProcDelegate(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam);

    public static void Register(
        string className,
        WndProcDelegate wndProc)
    {
        IntPtr hInstance =
            GetModuleHandle(null);

        WNDCLASSEXW wnd =
            new()
            {
                cbSize =
                    Marshal.SizeOf<WNDCLASSEXW>(),

                style =
                    CS_HREDRAW |
                    CS_VREDRAW,

                lpfnWndProc =
                    wndProc,

                cbClsExtra =
                    0,

                cbWndExtra =
                    0,

                hInstance =
                    hInstance,

                //
                // Standard arrow cursor.
                //
                hCursor =
                    LoadCursor(
                        IntPtr.Zero,
                        (IntPtr)32512),

                //
                // Important:
                // Avoid GDI background painting.
                // Prevents flicker/black flashes.
                //
                hbrBackground =
                    IntPtr.Zero,

                lpszMenuName =
                    null!,

                lpszClassName =
                    className,

                hIcon =
                    IntPtr.Zero,

                hIconSm =
                    IntPtr.Zero
            };

        ushort atom =
            RegisterClassExW(ref wnd);

        if (atom == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 1410) // 1410 = ERROR_CLASS_ALREADY_EXISTS
            {
                throw new InvalidOperationException(
                    $"RegisterClassExW failed with error code: {error}");
            }
        }
    }

    public static void Unregister(
        string className)
    {
        IntPtr hInstance =
            GetModuleHandle(null);

        UnregisterClassW(
            className,
            hInstance);
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(
        string? lpModuleName);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern ushort RegisterClassExW(
        ref WNDCLASSEXW lpwcx);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool UnregisterClassW(
        string lpClassName,
        IntPtr hInstance);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(
        IntPtr hInstance,
        IntPtr lpCursorName);
}