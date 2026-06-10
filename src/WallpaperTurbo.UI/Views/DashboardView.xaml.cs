using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
}
