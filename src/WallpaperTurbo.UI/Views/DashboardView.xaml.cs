using System;
using System.ComponentModel;
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
        StartupDiagnostics.StartTimer("DashboardView initialization");
        InitializeComponent();
        StartupDiagnostics.StopTimerWithMemory("DashboardView initialization");

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private MainViewModel? _mainVm;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
        {
            _mainVm = vm;
            _mainVm.PropertyChanged += OnMainVmPropertyChanged;
            ApplyPhotonState(vm.IsApplyingWallpaper);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_mainVm != null)
        {
            _mainVm.PropertyChanged -= OnMainVmPropertyChanged;
        }

        StopPhotons();
        _mainVm = null;
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsApplyingWallpaper))
        {
            ApplyPhotonState(_mainVm?.IsApplyingWallpaper ?? false);
        }
    }

    private void ApplyPhotonState(bool applying)
    {
        if (applying)
        {
            BeginPhotons();
        }
        else
        {
            StopPhotons();
        }
    }

    /// <summary>
    /// Starts the photon stream storyboards. These are begun from code-behind (rather than a
    /// Style DataTrigger) because the storyboards contain a Binding to the button's ActualWidth,
    /// and Storyboards that contain bindings cannot be frozen inside a Style — that throws a
    /// XamlParseException ("Cannot freeze this Storyboard timeline tree") on every render pass.
    /// </summary>
    private void BeginPhotons()
    {
        if (PhotonCanvas == null)
        {
            return;
        }

        PhotonCanvas.Opacity = 1;
        BeginPhoton("Photon1SB");
        BeginPhoton("Photon2SB");
        BeginPhoton("Photon3SB");
        BeginPhoton("Photon4SB");
    }

    private void BeginPhoton(string key)
    {
        if (PhotonCanvas.Resources[key] is Storyboard sb)
        {
            // 'this' resolves the Storyboard.TargetName (Photon1..4) in the view's namescope.
            sb.Begin(this, true);
        }
    }

    private void StopPhotons()
    {
        if (PhotonCanvas == null)
        {
            return;
        }

        foreach (var key in new[] { "Photon1SB", "Photon2SB", "Photon3SB", "Photon4SB" })
        {
            if (PhotonCanvas.Resources[key] is Storyboard sb)
            {
                sb.Stop(this);
            }
        }

        PhotonCanvas.Opacity = 0;
    }

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
