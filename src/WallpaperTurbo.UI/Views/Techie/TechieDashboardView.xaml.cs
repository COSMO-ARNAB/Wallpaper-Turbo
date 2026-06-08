using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Views;

public partial class TechieDashboardView : UserControl
{
    public TechieDashboardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Routes vertical mouse wheel from the horizontal ListBox to the parent ScrollViewer.
    /// </summary>
    private void OnRecentlyUsedMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        // Mark handled so the ListBox's internal ScrollViewer doesn't consume it
        e.Handled = true;

        // Bubble the event to the parent ScrollViewer so the dashboard scrolls vertically
        var parent = VisualTreeHelper.GetParent(sender as DependencyObject);
        while (parent is not ScrollViewer && parent != null)
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
}
