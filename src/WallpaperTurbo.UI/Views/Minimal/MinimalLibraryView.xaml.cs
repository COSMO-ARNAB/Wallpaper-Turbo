using System.Windows;
using System.Windows.Controls;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI.Views.Minimal;

public partial class MinimalLibraryView : UserControl
{
    private Window? _parentWindow;

    public MinimalLibraryView()
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
