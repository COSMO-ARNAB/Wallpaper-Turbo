namespace WallpaperTurbo.UI.Services.Theme;

public readonly record struct ThemeInputs(
    bool IsActive,
    bool IsPlaying,
    string BackdropPreference = "Mica",
    double GlassOpacity = 0.40);
