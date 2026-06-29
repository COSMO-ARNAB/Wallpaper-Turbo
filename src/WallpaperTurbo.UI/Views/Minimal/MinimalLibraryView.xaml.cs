using System.Windows;
using System.Windows.Controls;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Views.Minimal;

public partial class MinimalLibraryView : UserControl
{
    public MinimalLibraryView()
    {
        InitializeComponent();
    }

    private void OnCardMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 3)
        {
            if (sender is FrameworkElement element && element.DataContext is WallpaperEntry wp)
            {
                if (DataContext is LibraryViewModel vm)
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
