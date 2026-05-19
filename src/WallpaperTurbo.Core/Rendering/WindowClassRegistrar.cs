// WindowClassRegistrar.cs - Provides functionality to register and unregister window classes for wallpaper rendering in Wallpaper Turbo.
using System;
using System.Runtime.InteropServices;

namespace WallpaperTurbo.Core.Rendering;

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
                public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

public static class WindowClassRegistrar
{
    private const int CS_VREDRAW = 0x0001;
    private const int CS_HREDRAW = 0x0002;

    public static void Register(
        string className,
        WndProcDelegate wndProc)
    {
        var hInstance = GetModuleHandle(null);

        WNDCLASSEXW wnd = new WNDCLASSEXW();
        wnd.cbSize = Marshal.SizeOf<WNDCLASSEXW>();
        wnd.style = CS_HREDRAW | CS_VREDRAW;
        wnd.lpfnWndProc = wndProc;
        wnd.cbClsExtra = 0;
        wnd.cbWndExtra = 0;
        wnd.hInstance = hInstance;
        wnd.hCursor = LoadCursor(IntPtr.Zero, (IntPtr)32512);
        wnd.hbrBackground = CreateSolidBrush(RGB(0, 0, 0));
        wnd.lpszClassName = className;

        var atom = RegisterClassExW(ref wnd);

        if (atom == 0)
        {
            throw new InvalidOperationException("RegisterClassEx failed.");
        }
    }

    public static void Unregister(string className)
    {
        var hInstance = GetModuleHandle(null);
        UnregisterClassW(className, hInstance);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(
        string lpClassName,
        IntPtr hInstance);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(
        IntPtr hInstance,
        IntPtr lpCursorName);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    private static uint RGB(byte r, byte g, byte b)
    {
        return (uint)(r | (g << 8) | (b << 16));
    }
}