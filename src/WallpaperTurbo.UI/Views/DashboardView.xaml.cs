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
        InitializeComponent();
    }

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 3)
        {
            if (sender is FrameworkElement element && DataContext is DashboardViewModel vm)
            {
                WallpaperEntry? wp = null;
                string tag = element.Tag?.ToString() ?? string.Empty;
                if (tag == "Hero") wp = vm.HeroWallpaper;
                else if (tag == "SubHero1") wp = vm.SubHero1;
                else if (tag == "SubHero2") wp = vm.SubHero2;
                else if (tag == "SubHero3") wp = vm.SubHero3;

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
}
