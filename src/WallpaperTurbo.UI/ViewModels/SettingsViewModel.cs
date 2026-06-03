using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly WallpaperService _wallpaperService;
    private readonly UpdaterViewModel _updaterViewModel;
    private bool _suppressChannelUpdate;

    [ObservableProperty] private bool _useHardwareAcceleration = true;
    [ObservableProperty] private string _activePauseProfile = "Maximized";
    [ObservableProperty] private string _selectedLanguage = "English";
    [ObservableProperty] private string _activeAppVersion = "v1.0.0";
    [ObservableProperty] private string _engineLogsText = "No logs yet. AppRunner idle.";

    [ObservableProperty] private bool _autoUpdateEnabled = true;
    [ObservableProperty] private bool _checkOnStartup = true;
    [ObservableProperty] private string _selectedReleaseChannel = "Stable";

    public UpdaterViewModel Updater => _updaterViewModel;

    public SettingsViewModel(WallpaperService wallpaperService, UpdaterViewModel updaterViewModel)
    {
        _wallpaperService = wallpaperService;
        _updaterViewModel = updaterViewModel;
        _useHardwareAcceleration = !_wallpaperService.UseSoftwareDecoding;
        _activePauseProfile = _wallpaperService.ActivePauseProfile;

        // Hydrate updater-related fields from the persisted settings via the ViewModel
        var snapshot = _updaterViewModel.GetSettingsSnapshot();
        _autoUpdateEnabled = snapshot.AutoUpdateEnabled;
        _checkOnStartup = snapshot.CheckOnStartup;
        _selectedReleaseChannel = ChannelToDisplay(snapshot.ReleaseChannel);
        _activeAppVersion = "v" + _updaterViewModel.CurrentVersion;

        _ = LoadLogsAsync();
    }

    partial void OnUseHardwareAccelerationChanged(bool value)
    {
        _wallpaperService.UseSoftwareDecoding = !value;
    }

    partial void OnActivePauseProfileChanged(string value)
    {
        if (value != null)
        {
            _wallpaperService.ActivePauseProfile = value;
        }
    }

    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        _updaterViewModel.AutoUpdateEnabled = value;
    }

    partial void OnCheckOnStartupChanged(bool value)
    {
        _updaterViewModel.CheckOnStartup = value;
    }

    partial void OnSelectedReleaseChannelChanged(string value)
    {
        if (_suppressChannelUpdate) return;
        var channel = DisplayToChannel(value);
        _updaterViewModel.ReleaseChannel = channel;
    }

    public async Task LoadLogsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                // Try to find AppRunner output directory
                string appRunnerDir = baseDir;
                string localLog = Path.Combine(baseDir, "wallpaper.log");
                if (!File.Exists(localLog))
                {
                    string srcPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
                    appRunnerDir = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows", "win-x64");
                    localLog = Path.Combine(appRunnerDir, "wallpaper.log");
                }

                if (File.Exists(localLog))
                {
                    // Read last 15 lines of log file
                    var lines = File.ReadLines(localLog).TakeLast(15);
                    EngineLogsText = string.Join(Environment.NewLine, lines);
                }
                else
                {
                    EngineLogsText = "AppRunner engine log file (wallpaper.log) not generated yet. Start wallpaper to dump logs.";
                }
            }
            catch (Exception ex)
            {
                EngineLogsText = $"Error reading log file: {ex.Message}";
            }
        });
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        await LoadLogsAsync();
    }

    [RelayCommand]
    private async Task ResetAllSettingsAsync()
    {
        UseHardwareAcceleration = true;
        ActivePauseProfile = "Maximized";

        _suppressChannelUpdate = true;
        try
        {
            AutoUpdateEnabled = true;
            CheckOnStartup = true;
            SelectedReleaseChannel = "Stable";
        }
        finally
        {
            _suppressChannelUpdate = false;
        }
        _updaterViewModel.ApplySettings(new UpdaterSettings
        {
            AutoUpdateEnabled = true,
            CheckOnStartup = true,
            ReleaseChannel = ReleaseChannel.Stable
        });

        await Task.CompletedTask;
    }

    private static string ChannelToDisplay(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Stable => "Stable",
        ReleaseChannel.Preview => "Beta",
        ReleaseChannel.Nightly => "Dev",
        _ => "Stable"
    };

    private static ReleaseChannel DisplayToChannel(string display) => display switch
    {
        "Beta" => ReleaseChannel.Preview,
        "Dev" => ReleaseChannel.Nightly,
        _ => ReleaseChannel.Stable
    };
}
