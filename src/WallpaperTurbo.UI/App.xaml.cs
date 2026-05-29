using System;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace WallpaperTurbo.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly IServiceProvider _serviceProvider = ConfigureServices();
    private static Mutex? _appMutex;

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core Business Logic Services
        services.AddSingleton<Services.IThumbnailExtractor, Services.WpfThumbnailExtractor>();
        services.AddSingleton<Services.IWallpaperLibraryService, Services.WallpaperLibraryService>();
        services.AddSingleton<Services.WallpaperService>();
        services.AddSingleton<Services.TelemetryService>();
        services.AddSingleton<Services.IWallpaperPreviewService, Services.WallpaperPreviewService>();
        services.AddSingleton<Services.DiagnosticsService>(); // Development-time stability counters

        // ViewModels
        services.AddSingleton<ViewModels.MainViewModel>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        services.AddSingleton<ViewModels.LibraryViewModel>();
        services.AddSingleton<ViewModels.SettingsViewModel>();

        // Windows & Views
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    public static T GetService<T>() where T : class
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _appMutex = new Mutex(true, "WallpaperTurbo_UI_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Wallpaper Turbo is already running.", "Wallpaper Turbo", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
            return;
        }

        // Apply global application dark theme
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);

        // Resolve and display main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }
}
