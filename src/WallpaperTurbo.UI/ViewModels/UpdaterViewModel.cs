using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater;
using WallpaperTurbo.Updater.Events;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class UpdaterViewModel : ObservableObject, IDisposable
{
    private readonly UpdateCoordinator _coordinator;
    private readonly IUpdaterSettingsStore _settingsStore;
    private readonly Dispatcher _uiDispatcher;
    private UpdaterSettings _settings;
    private bool _disposed;

    [ObservableProperty] private string _currentVersion = "1.0.0";
    [ObservableProperty] private string _channelDisplay = "Stable";
    [ObservableProperty] private UpdateState _state = UpdateState.Idle;
    [ObservableProperty] private string _statusText = "Ready to check for updates.";
    [ObservableProperty] private string _statusDetailText = string.Empty;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _downloadSpeedText = string.Empty;
    [ObservableProperty] private string? _availableVersionText;
    [ObservableProperty] private string? _releaseNotes;
    [ObservableProperty] private string? _lastErrorMessage;
    [ObservableProperty] private bool _isNotificationVisible;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckButtonEnabled))]
    private bool _canCheck = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadButtonVisible))]
    private bool _canDownload;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallButtonVisible))]
    private bool _canInstall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCancelButtonVisible))]
    private bool _canCancel;

    public bool IsCheckButtonEnabled => CanCheck;
    public bool IsDownloadButtonVisible => CanDownload;
    public bool IsInstallButtonVisible => CanInstall;
    public bool IsCancelButtonVisible => CanCancel;

    // Progress fields for instantaneous speed calculation
    private DateTime _lastProgressTimestamp = DateTime.MinValue;
    private long _lastProgressBytes;

    public UpdaterViewModel(UpdateCoordinator coordinator, IUpdaterSettingsStore settingsStore)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _settings = _settingsStore.Load();
        _currentVersion = ResolveDisplayVersion();
        _channelDisplay = FormatChannelDisplay(_settings.ReleaseChannel);

        UpdaterDiagnostic.Log("UpdaterViewModel.ctor", $"Display version: {_currentVersion} | Channel (display): {_channelDisplay} | Channel (enum): {_settings.ReleaseChannel} | AutoUpdateEnabled={_settings.AutoUpdateEnabled} | CheckOnStartup={_settings.CheckOnStartup}");

        _coordinator.StateChanged += OnCoordinatorStateChanged;
        _coordinator.ProgressChanged += OnCoordinatorProgressChanged;
        _coordinator.UpdateAvailable += OnCoordinatorUpdateAvailable;
        _coordinator.ErrorOccurred += OnCoordinatorErrorOccurred;

        ApplyStateLocally(_coordinator.CurrentState);
    }

    public UpdaterSettings GetSettingsSnapshot() => _settings.Clone();

    public void ApplySettings(UpdaterSettings updated)
    {
        if (updated == null) throw new ArgumentNullException(nameof(updated));
        _settings = updated.Clone();
        _settingsStore.Save(_settings);
        ChannelDisplay = FormatChannelDisplay(_settings.ReleaseChannel);
    }

    public bool AutoUpdateEnabled
    {
        get => _settings.AutoUpdateEnabled;
        set
        {
            if (_settings.AutoUpdateEnabled == value) return;
            _settings.AutoUpdateEnabled = value;
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public bool CheckOnStartup
    {
        get => _settings.CheckOnStartup;
        set
        {
            if (_settings.CheckOnStartup == value) return;
            _settings.CheckOnStartup = value;
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public ReleaseChannel ReleaseChannel
    {
        get => _settings.ReleaseChannel;
        set
        {
            if (_settings.ReleaseChannel == value) return;
            _settings.ReleaseChannel = value;
            _settingsStore.Save(_settings);
            ChannelDisplay = FormatChannelDisplay(value);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await _coordinator.CheckForUpdatesAsync(_settings.ReleaseChannel);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdaterViewModel] CheckForUpdates threw: {ex.Message}");
            RunOnUi(() => SetErrorState("Failed to check for updates.", ex.Message));
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        try
        {
            IsNotificationVisible = false;
            await _coordinator.DownloadUpdateAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdaterViewModel] Download threw: {ex.Message}");
            RunOnUi(() => SetErrorState("Failed to download update.", ex.Message));
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        try
        {
            await _coordinator.InstallUpdateAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdaterViewModel] Install threw: {ex.Message}");
            RunOnUi(() => SetErrorState("Failed to launch installer.", ex.Message));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        try
        {
            _coordinator.CancelAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdaterViewModel] Cancel threw: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DismissNotification()
    {
        IsNotificationVisible = false;
    }

    public async Task RunStartupCheckAsync()
    {
        UpdaterDiagnostic.Log("UpdaterViewModel.RunStartupCheck", $"Entry. CheckOnStartup={_settings.CheckOnStartup} Channel={_settings.ReleaseChannel}");
        if (!_settings.AutoUpdateEnabled)
        {
            UpdaterDiagnostic.Log("UpdaterViewModel.RunStartupCheck", "AutoUpdateEnabled is false; skipping startup check.");
            Debug.WriteLine("[UpdaterViewModel] Auto updates disabled; skipping startup check.");
            return;
        }

        if (!_settings.CheckOnStartup)
        {
            UpdaterDiagnostic.Log("UpdaterViewModel.RunStartupCheck", "CheckOnStartup is false; skipping.");
            Debug.WriteLine("[UpdaterViewModel] CheckOnStartup disabled; skipping startup check.");
            return;
        }

        try
        {
            StartupDiagnostics.StartTimer("UpdateCoordinator startup check");
            UpdaterDiagnostic.Log("UpdaterViewModel.RunStartupCheck", $"Invoking coordinator.CheckForUpdatesAsync(channel={_settings.ReleaseChannel})");
            await _coordinator.CheckForUpdatesAsync(_settings.ReleaseChannel);
            UpdaterDiagnostic.Log("UpdaterViewModel.RunStartupCheck", $"Returned. State={_coordinator.CurrentState} Manifest={_coordinator.CurrentManifest?.Version.ToString() ?? "null"}");
        }
        catch (Exception ex)
        {
            UpdaterDiagnostic.Log("UpdaterViewModel.RunStartupCheck", $"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"[UpdaterViewModel] Startup check failed silently: {ex.Message}");
        }
        finally
        {
            StartupDiagnostics.StopTimerWithMemory("UpdateCoordinator startup check");
        }
    }

    private void OnCoordinatorStateChanged(object? sender, UpdateStateChangedEventArgs e)
    {
        RunOnUi(() => ApplyStateLocally(e.NewState));
    }

    private void OnCoordinatorProgressChanged(object? sender, UpdateProgress e)
    {
        RunOnUi(() => ApplyProgress(e));
    }

    private void OnCoordinatorUpdateAvailable(object? sender, UpdateManifest e)
    {
        RunOnUi(() =>
        {
            AvailableVersionText = e.Version.ToString();
            ReleaseNotes = string.IsNullOrWhiteSpace(e.ReleaseNotes) ? null : e.ReleaseNotes;
            StatusText = $"Update available: v{e.Version}";
            StatusDetailText = FormatBytes(e.FileSizeBytes) + " download";
            IsNotificationVisible = true;
        });
    }

    private void OnCoordinatorErrorOccurred(object? sender, UpdateErrorEventArgs e)
    {
        RunOnUi(() => SetErrorState(FriendlyErrorMessage(e), e.Exception?.Message));
    }

    private void ApplyStateLocally(UpdateState newState)
    {
        State = newState;
        IsBusy = newState is UpdateState.Checking
            or UpdateState.Downloading
            or UpdateState.Verifying
            or UpdateState.ShuttingDown
            or UpdateState.Installing;

        CanCheck = newState is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Failed or UpdateState.UpdateAvailable;
        CanDownload = newState == UpdateState.UpdateAvailable;
        CanInstall = newState == UpdateState.ReadyToInstall;
        CanCancel = newState is UpdateState.Checking or UpdateState.Downloading;

        switch (newState)
        {
            case UpdateState.Idle:
                StatusText = "Ready to check for updates.";
                StatusDetailText = string.Empty;
                ProgressPercent = 0;
                ProgressText = string.Empty;
                DownloadSpeedText = string.Empty;
                break;
            case UpdateState.Checking:
                StatusText = "Checking for updates…";
                StatusDetailText = string.Empty;
                break;
            case UpdateState.UpToDate:
                StatusText = $"You're up to date (v{CurrentVersion}).";
                StatusDetailText = string.Empty;
                AvailableVersionText = null;
                ReleaseNotes = null;
                IsNotificationVisible = false;
                break;
            case UpdateState.UpdateAvailable:
                if (string.IsNullOrEmpty(StatusText) || !StatusText.StartsWith("Update available", StringComparison.OrdinalIgnoreCase))
                {
                    StatusText = AvailableVersionText != null ? $"Update available: v{AvailableVersionText}" : "Update available.";
                }
                break;
            case UpdateState.Downloading:
                StatusText = "Downloading update…";
                _lastProgressTimestamp = DateTime.UtcNow;
                _lastProgressBytes = 0;
                break;
            case UpdateState.Downloaded:
                StatusText = "Download complete. Preparing to verify…";
                DownloadSpeedText = string.Empty;
                break;
            case UpdateState.Verifying:
                StatusText = "Verifying signature…";
                StatusDetailText = "Confirming authenticity of the installer.";
                break;
            case UpdateState.ReadyToInstall:
                StatusText = "Update ready to install.";
                StatusDetailText = "Click Install to apply the update. Wallpaper Turbo will restart.";
                ProgressPercent = 100;
                IsNotificationVisible = true;
                break;
            case UpdateState.ShuttingDown:
                StatusText = "Shutting down wallpaper engine…";
                StatusDetailText = "Closing components before launching the installer.";
                break;
            case UpdateState.Installing:
                StatusText = "Launching installer…";
                StatusDetailText = "The installer will now take over. Wallpaper Turbo will close.";
                break;
            case UpdateState.Failed:
                // StatusText is already set via SetErrorState. Keep it.
                break;
        }
    }

    private void ApplyProgress(UpdateProgress p)
    {
        ProgressPercent = Math.Clamp(p.PercentComplete, 0, 100);
        ProgressText = $"{FormatBytes(p.BytesDownloaded)} of {FormatBytes(p.TotalBytes)} ({ProgressPercent:0.0}%)";

        var now = DateTime.UtcNow;
        if (_lastProgressTimestamp != DateTime.MinValue)
        {
            var elapsed = (now - _lastProgressTimestamp).TotalSeconds;
            if (elapsed >= 0.5)
            {
                long delta = p.BytesDownloaded - _lastProgressBytes;
                if (delta > 0 && elapsed > 0)
                {
                    double bytesPerSec = delta / elapsed;
                    DownloadSpeedText = FormatBytes((long)bytesPerSec) + "/s";
                }
                _lastProgressTimestamp = now;
                _lastProgressBytes = p.BytesDownloaded;
            }
        }
        else
        {
            _lastProgressTimestamp = now;
            _lastProgressBytes = p.BytesDownloaded;
        }
    }

    private void SetErrorState(string headline, string? detail)
    {
        State = UpdateState.Failed;
        StatusText = headline;
        StatusDetailText = detail ?? string.Empty;
        LastErrorMessage = headline;
        IsBusy = false;
        CanCheck = true;
        CanDownload = false;
        CanInstall = false;
        CanCancel = false;
    }

    private static string FriendlyErrorMessage(UpdateErrorEventArgs e)
    {
        var msg = e.Message;
        if (string.IsNullOrWhiteSpace(msg))
        {
            msg = "An unexpected error occurred while updating.";
        }

        // Translate common technical conditions into actionable user messaging
        if (e.Exception is OperationCanceledException)
        {
            return "Update was cancelled.";
        }

        var exMessage = e.Exception?.Message ?? string.Empty;
        if (exMessage.IndexOf("no such host", StringComparison.OrdinalIgnoreCase) >= 0 ||
            exMessage.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
            exMessage.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
            e.Exception is HttpRequestException)
        {
            return "Couldn't reach the update server. Check your internet connection and try again.";
        }

        if (msg.IndexOf("signature", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "The downloaded update could not be verified. The file may be corrupt or tampered with.";
        }

        if (msg.IndexOf("shut down", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("wallpaper engine", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Couldn't stop the wallpaper engine cleanly. Close active wallpapers and try again.";
        }

        if (msg.IndexOf("install", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "The installer failed to launch. Try running Wallpaper Turbo as administrator.";
        }

        return msg;
    }

    private static string FormatChannelDisplay(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Stable => "Stable",
        ReleaseChannel.Preview => "Beta",
        ReleaseChannel.Nightly => "Dev",
        _ => channel.ToString()
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private static string ResolveDisplayVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (infoAttr != null && !string.IsNullOrWhiteSpace(infoAttr.InformationalVersion))
            {
                return infoAttr.InformationalVersion;
            }

            var version = assembly.GetName().Version;
            return version != null ? version.ToString(3) : "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _uiDispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _coordinator.StateChanged -= OnCoordinatorStateChanged;
            _coordinator.ProgressChanged -= OnCoordinatorProgressChanged;
            _coordinator.UpdateAvailable -= OnCoordinatorUpdateAvailable;
            _coordinator.ErrorOccurred -= OnCoordinatorErrorOccurred;
        }
        catch
        {
        }
    }
}
