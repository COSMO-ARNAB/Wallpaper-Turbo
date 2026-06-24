using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WallpaperTurbo.UI.Services;

public enum WindowBackdropMode
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
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
    private bool _disposed;

    [ObservableProperty]
    private bool _isWallpaperVisible;

    [ObservableProperty]
    private WindowBackdropMode _backdropMode = WindowBackdropMode.Mica;

    [ObservableProperty]
    private UIMaterialMode _materialMode = UIMaterialMode.Solid;

    public PresentationManager(WallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _wallpaperService.SessionStateChanged += OnSessionStateChanged;
        
        // Initial state sync
        SyncSessionState(_wallpaperService.ActiveSession);
    }

    private void OnSessionStateChanged(object? sender, WallpaperSessionEventArgs e)
    {
        SyncSessionState(e);
    }

    private void SyncSessionState(WallpaperSessionEventArgs? session)
    {
        bool visible = session != null && session.IsActive && session.IsPlaying;
        var dispatcher = Application.Current?.Dispatcher;
        
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            if (IsWallpaperVisible == visible)
                return;

            IsWallpaperVisible = visible;
            BackdropMode = visible ? WindowBackdropMode.Acrylic : WindowBackdropMode.Mica;
            MaterialMode = visible ? UIMaterialMode.Glass : UIMaterialMode.Solid;
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsWallpaperVisible == visible)
                    return;

                IsWallpaperVisible = visible;
                BackdropMode = visible ? WindowBackdropMode.Acrylic : WindowBackdropMode.Mica;
                MaterialMode = visible ? UIMaterialMode.Glass : UIMaterialMode.Solid;
            }));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wallpaperService.SessionStateChanged -= OnSessionStateChanged;
    }
}
