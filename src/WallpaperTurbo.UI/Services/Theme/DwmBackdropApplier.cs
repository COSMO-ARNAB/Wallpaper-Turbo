using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WallpaperTurbo.UI.Services.Theme;

/// <summary>
/// Pure DWM backdrop applier. Mirrors MainWindow.ApplyBackdropAttributes logic:
/// sets DWMWA_USE_IMMERSIVE_DARK_MODE (20) before DWMWA_SYSTEMBACKDROP_TYPE (38),
/// logs HRESULT failures, and returns early on hwnd == 0 (MainWindow handles Dispatcher defer).
/// No PresentationManager or Dispatcher dependency — intentionally dumb.
/// </summary>
public sealed class DwmBackdropApplier : IDwmApplier
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <inheritdoc />
    public void Apply(IntPtr hwnd, WindowBackdropMode mode)
    {
        if (hwnd == IntPtr.Zero)
        {
            Debug.WriteLine("[DwmBackdropApplier] Apply(IntPtr) dropped — hwnd is zero. Caller must ensure window is SourceInitialized.");
            return;
        }

        try
        {
            // 1) Immersive dark mode first — affects how Mica/Tabbed tints.
            // Must precede backdrop type so Mica renders with dark tint instead of light-grey fallback.
            int darkMode = 1;
            int hrDark = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            if (hrDark != 0)
            {
                Debug.WriteLine($"[DwmBackdropApplier] DwmSetWindowAttribute(20, darkMode) failed hr=0x{hrDark:X8}");
            }

            // 2) System backdrop type.
            int backdropType = (int)mode;
            int hrBackdrop = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            if (hrBackdrop != 0)
            {
                Debug.WriteLine($"[DwmBackdropApplier] DwmSetWindowAttribute(38, backdrop={backdropType}) failed hr=0x{hrBackdrop:X8} — window may appear grey");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply backdrop attributes: {ex.Message}");
        }
    }

    /// <summary>
    /// Convenience overload that resolves the HWND from a WPF Window and delegates to <see cref="Apply(IntPtr, WindowBackdropMode)"/>.
    /// </summary>
    /// <remarks>
    /// Caller must ensure window is SourceInitialized; MainWindow defers via Dispatcher if needed. This overload does not defer.
    /// Keep the applier pure/dumb; Dispatcher coupling stays in MainWindow. No Dispatcher.BeginInvoke fallback here by design.
    /// When hwnd==0 (window not yet SourceInitialized) the call is dropped after a Debug.WriteLine so the loss is observable.
    /// </remarks>
    public void Apply(Window window, WindowBackdropMode mode)
    {
        if (window == null)
        {
            return;
        }

        IntPtr hwnd;
        try
        {
            hwnd = new WindowInteropHelper(window).Handle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to resolve HWND from Window: {ex.Message}");
            return;
        }

        if (hwnd == IntPtr.Zero)
        {
            Debug.WriteLine("[DwmBackdropApplier] Apply(Window) dropped — hwnd is zero (window not yet SourceInitialized). Caller must ensure window is SourceInitialized; MainWindow defers via Dispatcher if needed. This overload does not defer.");
            return;
        }

        Apply(hwnd, mode);
    }
}
