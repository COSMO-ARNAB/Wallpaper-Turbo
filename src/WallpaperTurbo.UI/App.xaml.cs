using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.UI.Services.Theme;
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
        services.AddSingleton<WallpaperTurbo.Core.Hardware.IHardwareDetector, WallpaperTurbo.Core.Hardware.WindowsHardwareDetector>();
        services.AddSingleton<Services.WallpaperService>();
        services.AddSingleton<Services.PowerManagementService>();
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

        // Startup Coordinator
        services.AddSingleton<Services.WallpaperStartupCoordinator>();

        // Wallpaper visibility watchdog (desktop window truth)
        services.AddSingleton<WallpaperTurbo.Core.Services.Watchdog.IWallpaperVisibilityMonitor>(sp =>
            new WallpaperTurbo.Core.Services.Watchdog.WallpaperVisibilityWatchdog(
                new WallpaperTurbo.Core.Services.Watchdog.Win32WindowEnumerator(),
                1000,
                action => Application.Current?.Dispatcher?.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background)));

        // Theme resolution & DWM backdrop application (must precede PresentationManager/MainWindow)
        services.AddSingleton<IThemeResolver, ThemeResolver>();
        services.AddSingleton<IDwmApplier, DwmBackdropApplier>();

        // Power policy (pure decision function) + injectable clock for debouncing power events
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Services.Power.IBatterySaverPolicy, Services.Power.BatterySaverPolicy>();

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
        #if DEBUG
        Console.WriteLine("DEBUG: OnStartup Entry");
        #endif
        WallpaperTurbo.Updater.UpdaterDiagnostic.Init();
        #if DEBUG
        Console.WriteLine("DEBUG: UpdaterDiagnostic.Init completed");
        #endif
        WallpaperTurbo.Updater.UpdaterDiagnostic.Log("App.OnStartup", $"WPF app starting. Repo={UpdateRepoOwner}/{UpdateRepoName} Publisher={UpdatePublisherName}");
        #if DEBUG
        Console.WriteLine("DEBUG: Mutex creation starting");
        #endif
        bool createdNew = false;
        for (int retry = 1; retry <= 5; retry++)
        {
            _appMutex = new Mutex(true, "WallpaperTurbo_UI_Mutex", out createdNew);
            #if DEBUG
            Console.WriteLine($"DEBUG: Mutex attempt {retry}. createdNew={createdNew}");
            #endif
            if (createdNew)
            {
                break;
            }

            _appMutex.Close();
            _appMutex = null;

            // Activate existing instance. If a hung background instance is found, it will be cleaned up.
            var status = ActivateExistingInstanceOrCleanUp();
            if (status == InstanceStatus.HealthyInstanceActivated)
            {
                #if DEBUG
                Console.WriteLine("DEBUG: Healthy instance activated, shutting down immediately");
                #endif
                Application.Current?.Shutdown();
                return;
            }
            else if (status == InstanceStatus.StuckInstanceKilled)
            {
                #if DEBUG
                Console.WriteLine("DEBUG: Cleaned up stuck instance, retrying mutex immediately");
                #endif
                _appMutex = new Mutex(true, "WallpaperTurbo_UI_Mutex", out createdNew);
                if (createdNew)
                {
                    break;
                }
                _appMutex?.Close();
                _appMutex = null;
            }

            if (retry < 5)
            {
                #if DEBUG
                Console.WriteLine("DEBUG: Mutex still busy, sleeping 300ms before retry...");
                #endif
                System.Threading.Thread.Sleep(300);
            }
        }

        if (!createdNew)
        {
            #if DEBUG
            Console.WriteLine("DEBUG: Mutex still busy, shutting down");
            #endif
            Application.Current?.Shutdown();
            return;
        }

        #if DEBUG
        Console.WriteLine("DEBUG: Applying Theme");
        #endif
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

        // Initialize PowerManagementService to begin listening to Windows power line events
        _serviceProvider.GetRequiredService<Services.PowerManagementService>();

        Services.StartupDiagnostics.StartTimer("MainWindow resolve");
        #if DEBUG
        Console.WriteLine("DEBUG: Resolving MainWindow");
        #endif
        // Resolve and display main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        Services.StartupDiagnostics.StopTimerWithMemory("MainWindow resolve");

        Services.StartupDiagnostics.StartTimer("MainWindow.Show");
        #if DEBUG
        Console.WriteLine("DEBUG: Displaying MainWindow");
        #endif
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

        #if DEBUG
        Console.WriteLine("DEBUG: Setting up updater run check");
        #endif
        // Defer the non-blocking startup update check to after the window is shown,
        // so it never interferes with first-frame UI rendering. The check is gated
        // internally by UpdaterViewModel.RunStartupCheckAsync() on CheckOnStartup.
        var updater = _serviceProvider.GetRequiredService<ViewModels.UpdaterViewModel>();
        mainWindow.Dispatcher.BeginInvoke(new System.Action(() =>
        {
            #if DEBUG
            Console.WriteLine("DEBUG: Starting startup update check");
            #endif
            _ = updater.RunStartupCheckAsync();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // H2 cold-start: offload GPU preference registry sync off critical first-frame path.
        // Previously synchronous before base.OnStartup blocked Rendering by ~ File.Exists + registry IO.
        // Now fire-and-forget at ApplicationIdle, matching updater/startupCoordinator deferral.
        var wallpaperService = _serviceProvider.GetRequiredService<Services.WallpaperService>();
        mainWindow.Dispatcher.BeginInvoke(new Action(async () =>
        {
            await System.Threading.Tasks.Task.Run(() => wallpaperService.SyncGpuPreferences());
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // Start wallpaper engine coordination after UI is shown
        var startupCoordinator = _serviceProvider.GetRequiredService<Services.WallpaperStartupCoordinator>();
        mainWindow.Dispatcher.BeginInvoke(new System.Action(async () =>
        {
            #if DEBUG
            Console.WriteLine("DEBUG: Starting wallpaper engine coordination");
            #endif
            var result = await startupCoordinator.EnsureWallpaperRunningAsync();
            #if DEBUG
            Console.WriteLine($"DEBUG: Wallpaper coordination result: running={result.IsEngineRunning}, timeout={result.TimedOut}");
            #endif

            // Surface the outcome to the UI: on timeout the user gets
            // "Engine is still starting" with a Retry command instead of a silent hang
            var mainVm = _serviceProvider.GetService<ViewModels.MainViewModel>();
            mainVm?.ApplyStartupResult(result);

            // If startup declined to launch because of Battery Saver, tell the power service so it
            // treats this as *its own* suppression. Otherwise plugging in looks identical to the
            // user having stopped playback, and the wallpaper stays dead for the whole session.
            if (result.SuppressedByBatterySaver)
            {
                _serviceProvider.GetRequiredService<Services.PowerManagementService>()
                    .NotifyPlaybackSuppressedAtStartup();
            }
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        #if DEBUG
        Console.WriteLine("DEBUG: Calling base.OnStartup");
        #endif
        base.OnStartup(e);
        Services.StartupDiagnostics.LogWithMemory("App.OnStartup EXIT");
        #if DEBUG
        Console.WriteLine("DEBUG: OnStartup Exit");
        #endif
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

    private enum InstanceStatus
    {
        NoOtherInstance,
        StuckInstanceKilled,
        HealthyInstanceActivated
    }

    private static InstanceStatus ActivateExistingInstanceOrCleanUp()
    {
        try
        {
            using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            
            // Safety guard: never touch processes if named "dotnet"
            if (string.Equals(currentProcess.ProcessName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return InstanceStatus.NoOtherInstance;
            }

            var runningInstances = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(p => p.Id != currentProcess.Id)
                .ToList();

            bool killedStuckProcess = false;
            try
            {
                foreach (var process in runningInstances)
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                        return InstanceStatus.HealthyInstanceActivated;
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

            return killedStuckProcess ? InstanceStatus.StuckInstanceKilled : InstanceStatus.NoOtherInstance;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to handle existing instance activation: {ex.Message}");
            return InstanceStatus.NoOtherInstance;
        }
    }
}