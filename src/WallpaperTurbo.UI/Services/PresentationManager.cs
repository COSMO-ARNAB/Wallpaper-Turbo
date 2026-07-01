using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperTurbo.UI.Models;

namespace WallpaperTurbo.UI.Services;

public enum WindowBackdropMode
{
    // Values match DWMWA_SYSTEMBACKDROP_TYPE (attribute 38) constants:
    // DWMSBT_AUTO = 0, DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2,
    // DWMSBT_TRANSIENTWINDOW = 3 (Acrylic), DWMSBT_TABBEDWINDOW = 4.
    Auto = 0,
    None = 1,
    Mica = 2,
    Acrylic = 3,
    Transient = 3,
    Tabbed = 4
}

public enum UIMaterialMode
{
    Solid,
    Glass
}

public partial class PresentationManager : ObservableObject, IDisposable
{
    private readonly WallpaperService _wallpaperService;
    private readonly ISettingsStore _settingsStore;
    private bool _disposed;

    [ObservableProperty]
    private bool _isWallpaperVisible;

    [ObservableProperty]
    private WindowBackdropMode _backdropMode = WindowBackdropMode.Mica;

    [ObservableProperty]
    private UIMaterialMode _materialMode = UIMaterialMode.Solid;

    [ObservableProperty]
    private double _overlayOpacity = 1.0;

    public PresentationManager(WallpaperService wallpaperService, ISettingsStore settingsStore)
    {
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _wallpaperService.SessionStateChanged += OnSessionStateChanged;
        _settingsStore.SettingsChanged += OnSettingsStoreChanged;
        
        // Initial state sync
        SyncSessionState(_wallpaperService.ActiveSession);
    }

    private void OnSessionStateChanged(object? sender, WallpaperSessionEventArgs e)
    {
        SyncSessionState(e);
    }

    private void OnSettingsStoreChanged(object? sender, AppSettings e)
    {
        SyncSessionState(_wallpaperService.ActiveSession);
    }

    private void SyncSessionState(WallpaperSessionEventArgs? session)
    {
        bool visible = session != null && session.IsActive;
        var dispatcher = Application.Current?.Dispatcher;
        
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            ApplyState(visible);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => ApplyState(visible)));
        }
    }

    private void ApplyState(bool visible)
    {
        IsWallpaperVisible = visible;
        if (visible)
        {
            var settings = _settingsStore.Load();
            if (Enum.TryParse<WindowBackdropMode>(settings.GlassBackdrop, out var customBackdrop))
            {
                BackdropMode = customBackdrop;
            }
            else
            {
                BackdropMode = WindowBackdropMode.Acrylic;
            }
            OverlayOpacity = settings.GlassOpacity;
            MaterialMode = UIMaterialMode.Glass;
        }
        else
        {
            BackdropMode = WindowBackdropMode.None;
            OverlayOpacity = 1.0;
            MaterialMode = UIMaterialMode.Solid;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wallpaperService.SessionStateChanged -= OnSessionStateChanged;
        _settingsStore.SettingsChanged -= OnSettingsStoreChanged;
    }
}
