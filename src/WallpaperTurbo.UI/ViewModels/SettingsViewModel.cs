using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly WallpaperService _wallpaperService;

    [ObservableProperty] private bool _useHardwareAcceleration = true;
    [ObservableProperty] private string _activePauseProfile = "Maximized";
    [ObservableProperty] private string _selectedLanguage = "English";
    [ObservableProperty] private string _activeAppVersion = "v2.1.0";
    [ObservableProperty] private string _engineLogsText = "No logs yet. AppRunner idle.";

    public SettingsViewModel(WallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService;
        _useHardwareAcceleration = !_wallpaperService.UseSoftwareDecoding;
        _activePauseProfile = _wallpaperService.ActivePauseProfile;
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
        await Task.CompletedTask;
    }
}
