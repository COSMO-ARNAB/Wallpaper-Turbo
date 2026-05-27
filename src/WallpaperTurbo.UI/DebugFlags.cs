namespace WallpaperTurbo.UI;

/// <summary>
/// Centralized debug settings for runtime isolation and binary stabilization testing.
/// </summary>
public static class DebugFlags
{
    /// <summary>
    /// Master toggle to bypass all external wallpaper rendering, process creation, 
    /// WorkerW attachment, and VLC initialization.
    /// When true, replaces all background operations with logs and mock behaviors.
    /// </summary>
    public const bool SafeDebugMode = true;

    // ─────────────────────────────────────────────────────────────────────────
    // Runtime-Adjustable Crash Isolation Toggles (Active when SafeDebugMode = true)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles hover previews. When false, skips all StartPreviewAsync operations.
    /// </summary>
    public static bool EnableHoverPreviews { get; set; } = true;

    /// <summary>
    /// Toggles thumbnail eviction. When false, avoids clearing LoadedThumbnail properties.
    /// </summary>
    public static bool EnableThumbnailEviction { get; set; } = true;

    /// <summary>
    /// Toggles container virtualization. When false, forces the wrap panel to materialize 
    /// all library cards simultaneously (similar to standard WrapPanel behavior).
    /// </summary>
    public static bool EnableVirtualization { get; set; } = true;

    /// <summary>
    /// Toggles performance graph composition rendering. When false, updates graph paths 
    /// instantly without hooking CompositionTarget.Rendering, reducing CPU cycle pressure.
    /// </summary>
    public static bool EnableTelemetryInterpolation { get; set; } = true;

    /// <summary>
    /// Toggles async thumbnail loading. When false, decodes thumbnails synchronously 
    /// on the main UI dispatcher thread instead of delegating to Task.Run threadpool tasks.
    /// </summary>
    public static bool EnableAsyncThumbnailLoading { get; set; } = true;
}
