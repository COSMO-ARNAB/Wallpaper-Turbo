using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services.Theme;

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
    private readonly IThemeResolver _themeResolver;
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
        : this(wallpaperService, settingsStore, new ThemeResolver())
    {
    }

    public PresentationManager(WallpaperService wallpaperService, ISettingsStore settingsStore, IThemeResolver themeResolver)
    {
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _themeResolver = themeResolver ?? throw new ArgumentNullException(nameof(themeResolver));
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
        var dispatcher = Application.Current?.Dispatcher;
        
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            ApplyState(session);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => ApplyState(session)));
        }
    }

    private void ApplyState(WallpaperSessionEventArgs? session)
    {
        var settings = _settingsStore.Load();
        var inputs = new ThemeInputs(
            IsActive: session?.IsActive ?? false,
            IsPlaying: session?.IsPlaying ?? false,
            BackdropPreference: settings.GlassBackdrop ?? "Mica",
            GlassOpacity: settings.GlassOpacity);

        ThemeResult result = _themeResolver.Resolve(inputs);

        // Duplicate suppression: only set when value actually differs to avoid duplicate PropertyChanged
        // ObservableProperty's SetProperty also guards, but explicit check makes intent clear and
        // satisfies PresentationManagerTests.DoesNotTriggerDuplicateTransitions.
        if (IsWallpaperVisible != result.IsWallpaperVisible)
        {
            IsWallpaperVisible = result.IsWallpaperVisible;
        }

        if (BackdropMode != result.BackdropMode)
        {
            BackdropMode = result.BackdropMode;
        }

        if (!OverlayOpacity.Equals(result.OverlayOpacity))
        {
            OverlayOpacity = result.OverlayOpacity;
        }

        if (MaterialMode != result.MaterialMode)
        {
            MaterialMode = result.MaterialMode;
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
