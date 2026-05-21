// DesktopShellStrategy.cs

using System;
using System.Runtime.InteropServices;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class DesktopShellStrategy
    : IDesktopHostStrategy
{
    public string Name =>
        "Desktop Shell Strategy";

    public bool IsSupported()
    {
        //
        // Never use on modern raised desktop systems.
        //
        if (WindowsCapabilityDetector
            .HasRaisedDesktopComposition())
        {
            return false;
        }

        return NativeMethods.FindWindowW(
            "Progman",
            null) != IntPtr.Zero;
    }

    public bool TryAttach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(
            monitor);

        if (hwnd == IntPtr.Zero)
            return false;

        IntPtr progman =
            NativeMethods.FindWindowW(
                "Progman",
                null);

        if (progman == IntPtr.Zero)
            return false;

        ApplyLegacyStyles(hwnd);

        if (NativeMethods.SetParent(
                hwnd,
                progman) == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.RECT prct = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
        NativeMethods.MapWindowPoints(IntPtr.Zero, progman, ref prct, 2);

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            prct.Left,
            prct.Top,
            monitor.Width,
            monitor.Height,
            (uint)(
                NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE |
                NativeMethods.SetWindowPosFlags.SWP_SHOWWINDOW));

        return ValidateAttachment(
            hwnd,
            progman);
    }

    public void Detach(
        IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetParent(
            hwnd,
            IntPtr.Zero);
    }

    private static void ApplyLegacyStyles(
        IntPtr hwnd)
    {
        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        //
        // Remove popup behavior.
        //
        style &=
            unchecked((int)~(uint)
                NativeMethods.WindowStyles.WS_POPUP);

        style |=
            (int)(
                NativeMethods.WindowStyles.WS_CHILD |
                NativeMethods.WindowStyles.WS_VISIBLE);

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_STYLE,
            style);

        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        //
        // Modernized legacy fallback.
        //
        exStyle |=
            (int)(
                NativeMethods.WindowStyles.WS_EX_TOOLWINDOW |
                NativeMethods.WindowStyles.WS_EX_NOACTIVATE);

        exStyle &=
            unchecked((int)~(uint)
                NativeMethods.WindowStyles.WS_EX_APPWINDOW);

        exStyle &=
            unchecked((int)~(uint)
                NativeMethods.WindowStyles.WS_EX_LAYERED);

        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            exStyle);
    }

    private static bool ValidateAttachment(
        IntPtr hwnd,
        IntPtr expectedParent)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return false;

        if (!NativeMethods.IsWindowVisible(hwnd))
            return false;

        if (IsWindowCloaked(hwnd))
            return false;

        IntPtr parent =
            NativeMethods.GetParent(hwnd);

        Console.WriteLine(
            $"[Validate] ActualParent=0x{parent.ToInt64():X}");

        if (parent == IntPtr.Zero)
            return false;

        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        bool isChild =
            ((uint)style &
             (uint)NativeMethods.WindowStyles.WS_CHILD) != 0;

        return isChild;
    }

    private static bool IsWindowCloaked(
        IntPtr hwnd)
    {
        int cloaked =
            0;

        int result =
            DwmGetWindowAttribute(
                hwnd,
                14,
                out cloaked,
                Marshal.SizeOf<int>());

        return result == 0 &&
               cloaked != 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out int pvAttribute,
        int cbAttribute);
}