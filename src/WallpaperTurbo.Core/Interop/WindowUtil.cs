// WindowUtil.cs

using System;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Interop;

public static class WindowUtil
{
    private const int WS_CAPTION =
        0x00C00000;

    private const int WS_THICKFRAME =
        0x00040000;

    private const int WS_MINIMIZE =
        0x20000000;

    private const int WS_MAXIMIZEBOX =
        0x00010000;

    private const int WS_SYSMENU =
        0x00080000;

    private const int WS_EX_APPWINDOW =
        0x00040000;

    public static void BorderlessWinStyle(
        IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        style &=
            ~WS_CAPTION;

        style &=
            ~WS_THICKFRAME;

        style &=
            ~WS_MINIMIZE;

        style &=
            ~WS_MAXIMIZEBOX;

        style &=
            ~WS_SYSMENU;

        style &=
            unchecked((int)~(uint)
                NativeMethods.WindowStyles.WS_POPUP);
        
        style |=
            (int)(
                NativeMethods.WindowStyles.WS_CHILD |
                NativeMethods.WindowStyles.WS_VISIBLE |
                NativeMethods.WindowStyles.WS_CLIPSIBLINGS |
                NativeMethods.WindowStyles.WS_CLIPCHILDREN);

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_STYLE,
            style);

        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        //
        // (Removed explicit stripping of WS_EX_LAYERED to align with modern Win11 Raised Desktop requirements)
        //

        //
        // Remove taskbar presence.
        //
        exStyle &=
            ~WS_EX_APPWINDOW;

        //
        // Prevent activation/focus stealing and make transparent to mouse input.
        //
        exStyle |=
            (int)NativeMethods.WindowStyles.WS_EX_TOOLWINDOW;

        exStyle |=
            (int)NativeMethods.WindowStyles.WS_EX_NOACTIVATE;

        exStyle |=
            (int)NativeMethods.WindowStyles.WS_EX_TRANSPARENT;

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            exStyle);

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOMOVE |
                NativeMethods.SetWindowPosFlags.SWP_NOSIZE |
                NativeMethods.SetWindowPosFlags.SWP_NOZORDER |
                NativeMethods.SetWindowPosFlags.SWP_FRAMECHANGED |
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE));
    }

    public static bool TrySetParent(
    IntPtr child,
    IntPtr parent)
    {
        if (child == IntPtr.Zero ||
            parent == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.SetParent(
            child,
            parent);

        IntPtr actualParent =
            NativeMethods.GetParent(child);

        return actualParent == parent;
    }

    public static void SendToBottom(
        IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOMOVE |
                NativeMethods.SetWindowPosFlags.SWP_NOSIZE |
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                NativeMethods.SetWindowPosFlags.SWP_NOOWNERZORDER |
                NativeMethods.SetWindowPosFlags.SWP_NOSENDCHANGING));
    }

    public static bool HasExtendedStyle(
        IntPtr hwnd,
        NativeMethods.WindowStyles style)
    {
        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        return ((uint)exStyle &
                (uint)style) != 0;
    }

    public static IntPtr GetLastChildWindow(
        IntPtr parent)
    {
        IntPtr last =
            IntPtr.Zero;

        IntPtr current =
            IntPtr.Zero;

        while (true)
        {
            current =
                NativeMethods.FindWindowEx(
                    parent,
                    current,
                    null!,
                    null);

            if (current == IntPtr.Zero)
                break;

            last =
                current;
        }

        return last;
    }

    public static void SetWindowStyle(IntPtr hwnd, long styleToAdd)
    {
        int currentStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int newStyle = currentStyle | (int)styleToAdd;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, newStyle);
    }

    public static void SetWindowExStyle(IntPtr hwnd, long exStyleToAdd)
    {
        int currentExStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        int newExStyle = currentExStyle | (int)exStyleToAdd;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, newExStyle);
    }

    public static void SetWindowTransparency(IntPtr hwnd, byte transparency = 255)
    {
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & (int)NativeMethods.WindowStyles.WS_EX_LAYERED) == 0)
        {
            int newExStyle = exStyle | (int)NativeMethods.WindowStyles.WS_EX_LAYERED;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, newExStyle);
        }
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, transparency, 2); // 2 = LWA_ALPHA
    }

    public static void MakeChildrenTransparent(IntPtr parent)
    {
        if (parent == IntPtr.Zero) return;

        int width = 0;
        int height = 0;
        if (NativeMethods.GetClientRect(parent, out var parentRect))
        {
            width = parentRect.Right - parentRect.Left;
            height = parentRect.Bottom - parentRect.Top;
        }

        NativeMethods.EnumChildWindows(parent, (hwnd, lParam) =>
        {
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            style &= unchecked((int)~(uint)NativeMethods.WindowStyles.WS_POPUP);
            style |= (int)(
                NativeMethods.WindowStyles.WS_CHILD |
                NativeMethods.WindowStyles.WS_VISIBLE |
                NativeMethods.WindowStyles.WS_CLIPSIBLINGS |
                NativeMethods.WindowStyles.WS_CLIPCHILDREN);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);

            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle &= ~WS_EX_APPWINDOW;
            exStyle |= (int)(
                NativeMethods.WindowStyles.WS_EX_TOOLWINDOW |
                NativeMethods.WindowStyles.WS_EX_NOACTIVATE |
                NativeMethods.WindowStyles.WS_EX_TRANSPARENT);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            // Set both WS_EX_NOACTIVATE and WS_EX_TRANSPARENT to ensure VLC's child video windows are click-transparent and focus-free.
            SetWindowExStyle(hwnd, (long)(NativeMethods.WindowStyles.WS_EX_NOACTIVATE | NativeMethods.WindowStyles.WS_EX_TRANSPARENT));

            // Set layered window attributes with 255 alpha (opaque but hit-transparent due to WS_EX_TRANSPARENT + WS_EX_LAYERED)
            SetWindowTransparency(hwnd, 255);

            if (width > 0 && height > 0)
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    width,
                    height,
                    (uint)(
                        NativeMethods.SetWindowPosFlags.SWP_NOZORDER |
                        NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                        NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW |
                        NativeMethods.SetWindowPosFlags.SWP_FRAMECHANGED));
            }

            return true;
        }, IntPtr.Zero);
    }
}
