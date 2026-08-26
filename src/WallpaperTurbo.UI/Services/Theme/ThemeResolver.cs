using System;

namespace WallpaperTurbo.UI.Services.Theme;

public sealed class ThemeResolver : IThemeResolver
{
    public ThemeResult Resolve(ThemeInputs inputs)
    {
        bool visible = inputs.IsActive && inputs.IsPlaying;

        if (visible)
        {
            string setting = inputs.BackdropPreference ?? "Mica";

            WindowBackdropMode backdropMode;

            if (string.Equals(setting, "Mica", StringComparison.OrdinalIgnoreCase))
            {
                backdropMode = WindowBackdropMode.Tabbed;
            }
            else if (string.Equals(setting, "Glass", StringComparison.OrdinalIgnoreCase))
            {
                backdropMode = WindowBackdropMode.Transient;
            }
            else if (string.Equals(setting, "None", StringComparison.OrdinalIgnoreCase))
            {
                backdropMode = WindowBackdropMode.None;
            }
            else if (Enum.TryParse<WindowBackdropMode>(setting, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                backdropMode = parsed;
            }
            else
            {
                backdropMode = WindowBackdropMode.Tabbed;
            }

            return new ThemeResult(
                IsWallpaperVisible: true,
                BackdropMode: backdropMode,
                OverlayOpacity: inputs.GlassOpacity,
                MaterialMode: UIMaterialMode.Glass);
        }
        else
        {
            return new ThemeResult(
                IsWallpaperVisible: false,
                BackdropMode: WindowBackdropMode.Transient,
                OverlayOpacity: 1.0,
                MaterialMode: UIMaterialMode.Solid);
        }
    }
}
