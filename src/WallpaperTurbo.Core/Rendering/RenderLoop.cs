// RenderLoop.cs - Implements a basic message loop to keep the application responsive and process window messages for our rendering surface.
using System;
using System.Runtime.InteropServices;

namespace WallpaperTurbo.Core.Rendering;

public static class RenderLoop
{
    public static void Run()
    {
        while (GetMessage(
                   out MSG msg,
                   IntPtr.Zero,
                   0,
                   0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(
        out MSG lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(
        ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DispatchMessage(
        ref MSG lpMsg);
}