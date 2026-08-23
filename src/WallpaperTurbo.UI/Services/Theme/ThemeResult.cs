namespace WallpaperTurbo.UI.Services.Theme;

public readonly record struct ThemeResult(
    bool IsWallpaperVisible,
    WindowBackdropMode BackdropMode,
    double OverlayOpacity,
    UIMaterialMode MaterialMode);
