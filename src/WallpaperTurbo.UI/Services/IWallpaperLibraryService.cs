using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperTurbo.UI.Services;

/// <summary>
/// Defines a contract for managed storage, atomic manifest loading, merging, and transaction-safe importing of wallpapers.
/// </summary>
public interface IWallpaperLibraryService
{
    /// <summary>
    /// Raised when wallpaper metadata has been updated for an existing entry.
    /// </summary>
    event EventHandler<WallpaperEntry>? MetadataChanged;

    /// <summary>
    /// Asynchronously retrieves the merged list of default and user-imported wallpapers.
    /// </summary>
    /// <param name="cancellationToken">Coordinated token for cancel actions.</param>
    Task<IReadOnlyList<WallpaperEntry>> GetWallpapersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Transactionally imports a media file into managed storage, runs deduplication, and queues background thumbnail extraction.
    /// </summary>
    /// <param name="sourceFilePath">The source media file path.</param>
    /// <param name="onThumbnailCompleted">Callback fired when background dispatcher STA thumbnail finishes.</param>
    /// <param name="cancellationToken">Coordinated token for cancel actions.</param>
    Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default, IProgress<ImportProgress>? progress = null);

    /// <summary>
    /// Updates editable metadata for an imported wallpaper and persists it to both the manifest and metadata.json.
    /// </summary>
    Task<bool> UpdateWallpaperMetadataAsync(string guid, string? title, string? author, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully cancels outstanding background tasks and awaits manifest writes before app process exits.
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Deletes an imported wallpaper by removing it from the user manifest and deleting its folder on disk.
    /// </summary>
    Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default);
}

public sealed class ImportProgress
{
    public ImportProgress(int percent, string message)
    {
        Percent = Math.Clamp(percent, 0, 100);
        Message = message;
    }

    public int Percent { get; }
    public string Message { get; }
}
