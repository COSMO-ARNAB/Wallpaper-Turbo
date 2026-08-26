using System;
using System.Windows;

namespace WallpaperTurbo.UI.Services.Theme;

/// <summary>
/// Dumb DWM backdrop applier — pure P/Invoke, no dispatcher defer, no PresentationManager dependency.
/// MainWindow retains re-apply hooks (SourceInitialized, Activated, StateChanged) and hwnd==0 deferral.
/// </summary>
public interface IDwmApplier
{
    /// <summary>
    /// Applies immersive dark mode (attr 20) then system backdrop type (attr 38) to the given HWND.
    /// No-ops if hwnd is zero. Logs HRESULT failures via Debug.WriteLine.
    /// </summary>
    void Apply(IntPtr hwnd, WindowBackdropMode mode);

    /// <summary>
    /// Convenience overload that resolves the HWND from a WPF Window.
    /// No-ops if window is null or HWND is zero.
    /// </summary>
    void Apply(Window window, WindowBackdropMode mode);
}
