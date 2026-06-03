using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Updater;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly IServiceProvider _serviceProvider = ConfigureServices();
    private static Mutex? _appMutex;

    // Update source coordinates. Override these for fork or self-hosted release feeds.
    private const string UpdateRepoOwner = "WallpaperTurbo";
    private const string UpdateRepoName = "WallpaperTurbo";
    private const string UpdatePublisherName = "Wallpaper Turbo";

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

        // Updater Infrastructure (Phase C integration)
        services.AddSingleton<HttpClient>(_ =>
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("WallpaperTurbo-Updater", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        });
        services.AddSingleton<IUpdaterSettingsStore, Services.JsonUpdaterSettingsStore>();
        services.AddSingleton<IUpdateSourceProvider>(sp =>
            new GitHubReleaseProvider(sp.GetRequiredService<HttpClient>(), UpdateRepoOwner, UpdateRepoName));
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IDownloadManager>(sp =>
            new HttpDownloadManager(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<ISignatureValidator>(_ => new AuthenticodeValidator(UpdatePublisherName));
        services.AddSingleton<IUpdateApplier>(_ => new InnoSetupApplier());
        services.AddSingleton<IProcessManager, WindowsProcessManager>();
        services.AddSingleton<UpdateCoordinator>(sp => new UpdateCoordinator(
            sp.GetRequiredService<IUpdateService>(),
            sp.GetRequiredService<IDownloadManager>(),
            sp.GetRequiredService<ISignatureValidator>(),
            sp.GetRequiredService<IUpdateApplier>(),
            sp.GetRequiredService<IProcessManager>()));

        // ViewModels
        services.AddSingleton<ViewModels.MainViewModel>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        services.AddSingleton<ViewModels.LibraryViewModel>();
        services.AddSingleton<ViewModels.SettingsViewModel>();
        services.AddSingleton<ViewModels.UpdaterViewModel>();

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

        // Defer the non-blocking startup update check to after the window is shown,
        // so it never interferes with first-frame UI rendering. The check is gated
        // internally by UpdaterViewModel.RunStartupCheckAsync() on CheckOnStartup.
        var updater = _serviceProvider.GetRequiredService<ViewModels.UpdaterViewModel>();
        mainWindow.Dispatcher.BeginInvoke(new System.Action(() =>
        {
            _ = updater.RunStartupCheckAsync();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Dispose the coordinator so it can unsubscribe from underlying service events
            if (_serviceProvider.GetService<Updater.UpdateCoordinator>() is IDisposable disposableCoordinator)
            {
                disposableCoordinator.Dispose();
            }
        }
        catch { /* best-effort cleanup */ }

        base.OnExit(e);
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
