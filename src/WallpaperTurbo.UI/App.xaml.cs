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
    private static IServiceProvider _serviceProvider = ConfigureServices();
    private static Mutex? _appMutex;

    internal static void SetTestServiceProvider(IServiceProvider provider)
    {
        _serviceProvider = provider;
    }

    // Update source coordinates. Override these for fork or self-hosted release feeds.
    private const string UpdateRepoOwner = "COSMO-ARNAB";
    private const string UpdateRepoName = "Wallpaper-Turbo";
    private const string UpdatePublisherName = "COSMO-ARNAB";

    private static IServiceProvider ConfigureServices()
    {
        Services.StartupDiagnostics.Initialize();
        Services.StartupDiagnostics.StartTimer("ServiceProvider build");
        var services = new ServiceCollection();

        // Core Business Logic Services
        services.AddSingleton<Services.IThumbnailExtractor, Services.WpfThumbnailExtractor>();
        services.AddSingleton<Services.IWallpaperLibraryService, Services.WallpaperLibraryService>();
        services.AddSingleton<Services.ISettingsStore, Services.JsonSettingsStore>();
        services.AddSingleton<Services.IGpuPreferenceService, Services.WindowsGpuPreferenceService>();
        services.AddSingleton<Services.WallpaperService>();
        services.AddSingleton<Services.TelemetryService>();
        services.AddSingleton<Services.IWallpaperPreviewService, Services.WallpaperPreviewService>();
        services.AddSingleton<Services.DiagnosticsService>(); // Development-time stability counters

        // Layout Infrastructure
        services.AddSingleton<Services.ILayoutPreferenceStore, Services.SettingsStoreLayoutPreferenceStore>();

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
        services.AddSingleton<ViewModels.LayoutHostViewModel>();
        services.AddSingleton<ViewModels.MainViewModel>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        services.AddSingleton<ViewModels.LibraryViewModel>();
        services.AddSingleton<ViewModels.SettingsViewModel>();
        services.AddSingleton<ViewModels.UpdaterViewModel>();

        // Presentation Management
        services.AddSingleton<Services.PresentationManager>();

        // Windows & Views
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider();
        Services.StartupDiagnostics.StopTimerWithMemory("ServiceProvider build");
        return provider;
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
        if (Application.Current != null)
        {
            Application.Current.DispatcherUnhandledException += (s, ev) =>
            {
                Services.StartupDiagnostics.LogException("Application.Current.DispatcherUnhandledException", ev.Exception);
                ev.Handled = true;
            };
        }

        Services.StartupDiagnostics.LogWithMemory("App.OnStartup ENTRY");
        Console.WriteLine("DEBUG: OnStartup Entry");
        WallpaperTurbo.Updater.UpdaterDiagnostic.Init();
        Console.WriteLine("DEBUG: UpdaterDiagnostic.Init completed");
        WallpaperTurbo.Updater.UpdaterDiagnostic.Log("App.OnStartup", $"WPF app starting. Repo={UpdateRepoOwner}/{UpdateRepoName} Publisher={UpdatePublisherName}");
        Console.WriteLine("DEBUG: Mutex creation starting");
        _appMutex = new Mutex(true, "WallpaperTurbo_UI_Mutex", out bool createdNew);
        Console.WriteLine($"DEBUG: Mutex created. createdNew={createdNew}");
        if (!createdNew)
        {
            Console.WriteLine("DEBUG: Mutex not created, activating existing instance");
            // Activate existing instance. If a hung background instance is found, it will be cleaned up.
            if (ActivateExistingInstanceOrCleanUp())
            {
                Console.WriteLine("DEBUG: Cleaned up existing instance, retrying mutex");
                // Try to acquire the Mutex again since the hung instance was killed
                _appMutex?.Close();
                _appMutex = new Mutex(true, "WallpaperTurbo_UI_Mutex", out createdNew);
            }
            
            if (!createdNew)
            {
                Console.WriteLine("DEBUG: Mutex still busy, shutting down");
                Application.Current?.Shutdown();
                return;
            }
        }

        Console.WriteLine("DEBUG: Applying Theme");
        var settingsStore = _serviceProvider.GetRequiredService<Services.ISettingsStore>();
        var settings = settingsStore.Load();
        if (string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase))
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
        }
        else if (string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
        }
        else
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        }

        Services.StartupDiagnostics.StartTimer("MainWindow resolve");
        Console.WriteLine("DEBUG: Resolving MainWindow");
        // Resolve and display main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        Services.StartupDiagnostics.StopTimerWithMemory("MainWindow resolve");

        Services.StartupDiagnostics.StartTimer("MainWindow.Show");
        Console.WriteLine("DEBUG: Displaying MainWindow");
        mainWindow.Show();
        Services.StartupDiagnostics.StopTimerWithMemory("MainWindow.Show");

        // Hook composition target for first frame rendering
        EventHandler? renderingHandler = null;
        renderingHandler = (sender, args) =>
        {
            System.Windows.Media.CompositionTarget.Rendering -= renderingHandler;
            Services.StartupDiagnostics.LogWithMemory("FIRST_FRAME_RENDERED");
        };
        System.Windows.Media.CompositionTarget.Rendering += renderingHandler;

        Console.WriteLine("DEBUG: Setting up updater run check");
        // Defer the non-blocking startup update check to after the window is shown,
        // so it never interferes with first-frame UI rendering. The check is gated
        // internally by UpdaterViewModel.RunStartupCheckAsync() on CheckOnStartup.
        var updater = _serviceProvider.GetRequiredService<ViewModels.UpdaterViewModel>();
        mainWindow.Dispatcher.BeginInvoke(new System.Action(() =>
        {
            Console.WriteLine("DEBUG: Starting startup update check");
            _ = updater.RunStartupCheckAsync();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        Console.WriteLine("DEBUG: Calling base.OnStartup");
        base.OnStartup(e);
        Services.StartupDiagnostics.LogWithMemory("App.OnStartup EXIT");
        Console.WriteLine("DEBUG: OnStartup Exit");
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

        try
        {
            // Dispose MainViewModel to unsubscribe from telemetry events (D-1 Part A)
            if (_serviceProvider.GetService<ViewModels.MainViewModel>() is IDisposable disposableMainViewModel)
            {
                disposableMainViewModel.Dispose();
            }
        }
        catch { /* best-effort cleanup */ }

        try
        {
            // Dispose PresentationManager to clean up session event handler
            if (_serviceProvider.GetService<Services.PresentationManager>() is IDisposable disposablePresentation)
            {
                disposablePresentation.Dispose();
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
            using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            
            // Safety guard: never touch processes if named "dotnet"
            if (string.Equals(currentProcess.ProcessName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var runningInstances = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(p => p.Id != currentProcess.Id)
                .ToList();

            try
            {
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
            finally
            {
                foreach (var process in runningInstances)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to handle existing instance activation: {ex.Message}");
        }
        
        return killedStuckProcess;
    }
}
