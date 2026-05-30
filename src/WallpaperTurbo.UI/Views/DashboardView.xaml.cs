using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 3)
        {
            if (sender is FrameworkElement element && DataContext is DashboardViewModel vm)
            {
                WallpaperEntry? wp = element.DataContext as WallpaperEntry;
                if (wp == null)
                {
                    string tag = element.Tag?.ToString() ?? string.Empty;
                    if (tag == "Hero") wp = vm.HeroWallpaper;
                    else if (tag == "SubHero1") wp = vm.SubHero1;
                    else if (tag == "SubHero2") wp = vm.SubHero2;
                    else if (tag == "SubHero3") wp = vm.SubHero3;
                }

                if (wp != null)
                {
                    if (vm.TripleClickPlayWallpaperCommand.CanExecute(wp))
                    {
                        vm.TripleClickPlayWallpaperCommand.Execute(wp);
                    }
                }
            }
        }
    }

    private double _targetHorizontalOffset = -1;

    private void OnRecentlyUsedMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            var scrollViewer = GetScrollViewer(listBox);
            if (scrollViewer != null)
            {
                e.Handled = true;

                // Sync target offset if it was changed by other means (or on first run)
                if (_targetHorizontalOffset < 0 || Math.Abs(_targetHorizontalOffset - scrollViewer.HorizontalOffset) > 10.0)
                {
                    _targetHorizontalOffset = scrollViewer.HorizontalOffset;
                }

                // Adjust multiplier for horizontal speed
                _targetHorizontalOffset -= e.Delta * 0.8;
                
                // Clamp within bounds
                _targetHorizontalOffset = Math.Max(0, Math.Min(_targetHorizontalOffset, scrollViewer.ScrollableWidth));

                // Animate horizontal offset smoothly
                var animation = new DoubleAnimation
                {
                    To = _targetHorizontalOffset,
                    Duration = TimeSpan.FromMilliseconds(250),
                    DecelerationRatio = 0.8
                };

                scrollViewer.BeginAnimation(ScrollViewerHelper.AnimateHorizontalOffsetProperty, animation);
            }
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}

public static class ScrollViewerHelper
{
    public static readonly DependencyProperty AnimateHorizontalOffsetProperty =
        DependencyProperty.RegisterAttached("AnimateHorizontalOffset", typeof(double), typeof(ScrollViewerHelper),
            new FrameworkPropertyMetadata(0.0, OnAnimateHorizontalOffsetChanged));

    public static double GetAnimateHorizontalOffset(DependencyObject obj) => (double)obj.GetValue(AnimateHorizontalOffsetProperty);
    public static void SetAnimateHorizontalOffset(DependencyObject obj, double value) => obj.SetValue(AnimateHorizontalOffsetProperty, value);

    private static void OnAnimateHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToHorizontalOffset((double)e.NewValue);
        }
    }
}
