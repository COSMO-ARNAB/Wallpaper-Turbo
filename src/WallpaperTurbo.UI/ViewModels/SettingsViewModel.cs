using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly WallpaperService _wallpaperService;
    private readonly UpdaterViewModel _updaterViewModel;
    private readonly LayoutHostViewModel _layoutHostViewModel;
    private bool _suppressChannelUpdate;

    [ObservableProperty] private bool _useHardwareAcceleration = true;
    [ObservableProperty] private string _activePauseProfile = "Maximized";
    [ObservableProperty] private string _selectedLanguage = "English";
    [ObservableProperty] private string _activeAppVersion = "v1.0.0";
    [ObservableProperty] private string _engineLogsText = "No logs yet. AppRunner idle.";

    [ObservableProperty] private bool _autoUpdateEnabled = true;
    [ObservableProperty] private bool _checkOnStartup = true;
    [ObservableProperty] private string _selectedReleaseChannel = "Stable";

    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private string _selectedLayout = "Minimal";
    [ObservableProperty] private bool _pauseOnFullscreen = true;
    [ObservableProperty] private bool _muteWallpaperAudio = true;
    [ObservableProperty] private bool _autoStartWallpaperEngine = true;
    [ObservableProperty] private bool _rememberLastWallpaper = true;
    [ObservableProperty] private string _performanceMode = "Balanced";
    [ObservableProperty] private bool _batterySaverEnabled = false;

    public UpdaterViewModel Updater => _updaterViewModel;
    public LayoutHostViewModel LayoutHost => _layoutHostViewModel;

    public SettingsViewModel(
        WallpaperService wallpaperService, 
        UpdaterViewModel updaterViewModel, 
        LayoutHostViewModel layoutHostViewModel)
    {
        _wallpaperService = wallpaperService;
        _updaterViewModel = updaterViewModel;
        _layoutHostViewModel = layoutHostViewModel;
        _useHardwareAcceleration = !_wallpaperService.UseSoftwareDecoding;
        _activePauseProfile = _wallpaperService.ActivePauseProfile;

        // Initialize SelectedLayout from current layout
        _selectedLayout = _layoutHostViewModel.CurrentLayout.ToString();

        // Initialize PauseOnFullscreen based on ActivePauseProfile
        _pauseOnFullscreen = _activePauseProfile != "Disabled" && _activePauseProfile != "None";

        // Determine the current UI theme
        var currentTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        _selectedTheme = currentTheme switch
        {
            Wpf.Ui.Appearance.ApplicationTheme.Light => "Light",
            Wpf.Ui.Appearance.ApplicationTheme.Dark => "Dark",
            _ => "System"
        };

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
            PauseOnFullscreen = value != "Disabled" && value != "None";
        }
    }

    partial void OnPauseOnFullscreenChanged(bool value)
    {
        ActivePauseProfile = value ? "Maximized" : "Disabled";
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase))
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
        }
        else if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
        }
        else
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        }
    }

    partial void OnSelectedLayoutChanged(string value)
    {
        if (Enum.TryParse<LayoutMode>(value, out var layoutMode))
        {
            _layoutHostViewModel.SwitchLayout(layoutMode);
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
        PauseOnFullscreen = true;
        SelectedTheme = "System";
        SelectedLayout = "Minimal";
        MuteWallpaperAudio = true;
        AutoStartWallpaperEngine = true;
        RememberLastWallpaper = true;
        PerformanceMode = "Balanced";
        BatterySaverEnabled = false;

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

    [RelayCommand]
    private void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }
}
