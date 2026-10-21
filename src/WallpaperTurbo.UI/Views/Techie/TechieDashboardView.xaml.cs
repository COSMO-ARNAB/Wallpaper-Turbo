using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Views;

public partial class TechieDashboardView : UserControl
{
    private Window? _parentWindow;

    public TechieDashboardView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _parentWindow = Window.GetWindow(this);
        if (_parentWindow != null)
        {
            _parentWindow.Activated += OnWindowFocusChanged;
            _parentWindow.Deactivated += OnWindowFocusChanged;
            _parentWindow.StateChanged += OnWindowFocusChanged;
        }

        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_parentWindow != null)
        {
            _parentWindow.Activated -= OnWindowFocusChanged;
            _parentWindow.Deactivated -= OnWindowFocusChanged;
            _parentWindow.StateChanged -= OnWindowFocusChanged;
            _parentWindow = null;
        }

        IsVisibleChanged -= OnIsVisibleChanged;
    }

    private void OnWindowFocusChanged(object? sender, System.EventArgs e) => HandlePreviewGating();
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => HandlePreviewGating();

    private void HandlePreviewGating()
    {
        var settingsStore = App.GetService<ISettingsStore>();
        bool lowPower = settingsStore?.Load().LowPowerStaticPreview ?? false;
        bool isWindowActive = _parentWindow?.IsActive ?? true;
        bool isMinimized = _parentWindow?.WindowState == WindowState.Minimized;
        bool shouldPause = !IsVisible || !isWindowActive || isMinimized || lowPower;

        if (shouldPause)
        {
            try { _ = App.GetService<IWallpaperPreviewService>()?.StopPreviewAsync(); } catch { }
        }
    }

    /// <summary>
    /// Routes vertical mouse wheel from the horizontal ListBox to the parent ScrollViewer.
    /// </summary>
    private void OnRecentlyUsedMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        if (sender is DependencyObject depObj)
        {
            var scrollViewer = FindScrollViewer(depObj);
            if (scrollViewer != null && scrollViewer.ScrollableWidth > 0)
            {
                // Scroll horizontally by a moderate amount (e.g. 48 pixels per 120 delta)
                double step = e.Delta * 0.4;
                double targetOffset = scrollViewer.HorizontalOffset - step;
                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableWidth)));
                
                e.Handled = true;
                return;
            }
        }

        // If the list is not scrollable horizontally, bubble the scroll event to the parent vertical ScrollViewer
        var parent = VisualTreeHelper.GetParent(sender as DependencyObject);
        while (parent is not ScrollViewer && parent != null)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (parent is ScrollViewer parentScrollViewer)
        {
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            parentScrollViewer.RaiseEvent(eventArg);
        }
    }

    /// <summary>
    /// Triple-click card handler — plays the clicked wallpaper.
    /// </summary>
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

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer s) return s;

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
