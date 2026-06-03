using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Views;

public partial class DashboardView : UserControl
{
    // ── Win32 ────────────────────────────────────────────────────────────────
    // WM_MOUSEHWHEEL (0x020E) — fired by precision touchpad horizontal swipes.
    // WPF never surfaces this as a routed event, so we hook the message pump.
    private const int WM_MOUSEHWHEEL = 0x020E;

    // ── Inertia scroll state ──────────────────────────────────────────────────
    // Physics constants — tune here if feel needs adjustment.
    private const double Friction         = 0.95;  // velocity multiplied per frame (higher = longer, gentler glide)
    private const double StopThreshold    = 0.15;  // px/frame — loop stops below this (lower = smoother deceleration tail)
    private const double ScrollSensitivity = 0.20; // input delta → velocity scale (lower = slower, less abrupt onset)

    private double _velocity;           // current scroll velocity (px/frame)
    private double _currentOffset;      // tracked horizontal offset
    private bool   _renderLoopActive;   // guards Rendering subscription

    // ── Cached references (populated once at Loaded) ──────────────────────────
    private HwndSource?  _hwndSource;
    private ScrollViewer? _cachedScrollViewer;

    // ─────────────────────────────────────────────────────────────────────────
    public DashboardView()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Hook horizontal wheel messages from the OS.
        var window = Window.GetWindow(this);
        if (window != null)
        {
            _hwndSource = PresentationSource.FromVisual(window) as HwndSource;
            _hwndSource?.AddHook(WndProc);
        }

        // Initialize rendering hooks if needed, but do NOT cache the ScrollViewer yet.
        // It may be collapsed if IsLoading is true, causing FindScrollViewer to return null.
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Remove the WndProc hook.
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;

        // CRITICAL: Always detach from CompositionTarget.Rendering when the
        // view is removed from the visual tree. Failing to do this creates a
        // permanent reference from the static event → this view → the whole
        // visual subtree, leaking memory every time the user navigates away.
        StopRenderLoop();
        _cachedScrollViewer = null;
    }

    // ── WndProc hook — horizontal touchpad swipe ──────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEHWHEEL)
        {
            // High-word of wParam is the wheel delta.
            // Positive = swipe RIGHT (should increase HorizontalOffset).
            int rawDelta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));

            var sv = GetScrollViewer();
            if (sv != null && IsMouseOverScrollViewer(sv))
            {
                // Positive raw delta → scroll RIGHT → positive velocity change.
                AccumulateVelocity(rawDelta);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    // ── Route vertical wheel to parent ScrollViewer ──────────────────────────

    private void OnRecentlyUsedMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        // Mark handled so the ListBox's internal ScrollViewer doesn't consume it
        e.Handled = true;

        // Bubble the event to the parent ScrollViewer so the dashboard scrolls vertically
        var parent = VisualTreeHelper.GetParent(sender as DependencyObject);
        while (parent != null && parent is not ScrollViewer)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (parent is ScrollViewer parentScrollViewer)
        {
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            parentScrollViewer.RaiseEvent(eventArg);
        }
    }

    // ── Inertia engine ────────────────────────────────────────────────────────

    /// <summary>
    /// Adds the scaled <paramref name="rawDelta"/> to the current velocity and
    /// starts the render loop if it is not already running.
    /// </summary>
    private void AccumulateVelocity(double rawDelta)
    {
        var sv = GetScrollViewer();
        if (sv == null) return;

        // If the loop is idle, seed _currentOffset from the actual scroll position
        // so we don't jump on the first frame.
        if (!_renderLoopActive)
            _currentOffset = sv.HorizontalOffset;

        _velocity += rawDelta * ScrollSensitivity;
        StartRenderLoop();
    }

    /// <summary>
    /// Called once per rendered frame by the compositor.
    /// Applies friction, updates scroll position, and self-terminates when settled.
    /// </summary>
    private void OnRenderFrame(object? sender, EventArgs e)
    {
        var sv = GetScrollViewer();
        if (sv == null)
        {
            StopRenderLoop();
            return;
        }

        // Decay velocity.
        _velocity *= Friction;

        // Stop the loop once motion is imperceptible.
        if (Math.Abs(_velocity) < StopThreshold)
        {
            _velocity = 0;
            StopRenderLoop();
            return;
        }

        // Clamp and apply.
        _currentOffset = Math.Max(0, Math.Min(_currentOffset + _velocity, sv.ScrollableWidth));
        sv.ScrollToHorizontalOffset(_currentOffset);
    }

    private void StartRenderLoop()
    {
        if (_renderLoopActive) return;
        CompositionTarget.Rendering += OnRenderFrame;
        _renderLoopActive = true;
    }

    private void StopRenderLoop()
    {
        if (!_renderLoopActive) return;
        CompositionTarget.Rendering -= OnRenderFrame;
        _renderLoopActive = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsMouseOverScrollViewer(ScrollViewer sv)
    {
        try
        {
            if (sv is IInputElement inputElement)
            {
                var pos = Mouse.GetPosition(inputElement);
                return pos.X >= 0 && pos.Y >= 0
                    && pos.X <= sv.ActualWidth
                    && pos.Y <= sv.ActualHeight;
            }
        }
        catch { /* sv not yet in visual tree */ }
        return false;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        return null;
    }

    private ScrollViewer? GetScrollViewer()
    {
        if (_cachedScrollViewer != null)
            return _cachedScrollViewer;

        if (FindName("RecentlyUsedListBox") is ListBox listBox)
        {
            _cachedScrollViewer = FindScrollViewer(listBox);
        }
        return _cachedScrollViewer;
    }

    // ── Triple-click card handler ─────────────────────────────────────────────

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 3) return;
        if (sender is not FrameworkElement element) return;
        if (DataContext is not DashboardViewModel vm) return;

        WallpaperEntry? wp = element.DataContext as WallpaperEntry;
        if (wp == null)
        {
            string tag = element.Tag?.ToString() ?? string.Empty;
            wp = tag switch
            {
                "Hero"     => vm.CurrentWallpaper,
                "SubHero1" => vm.SubHero1,
                "SubHero2" => vm.SubHero2,
                "SubHero3" => vm.SubHero3,
                _          => null
            };
        }

        if (wp != null && vm.TripleClickPlayWallpaperCommand.CanExecute(wp))
            vm.TripleClickPlayWallpaperCommand.Execute(wp);
    }
}
