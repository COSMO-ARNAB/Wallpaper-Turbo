using System;
using System.Linq;
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        _appMutex = new Mutex(true, "WallpaperTurbo_UI_Mutex", out bool createdNew);
        if (!createdNew)
        {
            // Activate existing instance. If a hung background instance is found, it will be cleaned up.
            if (ActivateExistingInstanceOrCleanUp())
            {
                // Try to acquire the Mutex again since the hung instance was killed
                _appMutex?.Close();
                _appMutex = new Mutex(true, "WallpaperTurbo_UI_Mutex", out createdNew);
            }
            
            if (!createdNew)
            {
                Application.Current.Shutdown();
                return;
            }
        }

        // Apply global application dark theme
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);

        // Resolve and display main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    private static bool ActivateExistingInstanceOrCleanUp()
    {
        bool killedStuckProcess = false;
        try
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            
            // Safety guard: never touch processes if named "dotnet"
            if (string.Equals(currentProcess.ProcessName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var runningInstances = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(p => p.Id != currentProcess.Id)
                .ToList();

            foreach (var process in runningInstances)
            {
                IntPtr handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                    return false;
                }
                
                // If it has no main window handle and has been running for more than 10 seconds,
                // it is likely a stuck background process that was not terminated properly.
                try
                {
                    if ((DateTime.Now - process.StartTime).TotalSeconds > 10)
                    {
                        process.Kill();
                        process.WaitForExit(1500);
                        killedStuckProcess = true;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to handle existing instance activation: {ex.Message}");
        }
        
        return killedStuckProcess;
    }
}
