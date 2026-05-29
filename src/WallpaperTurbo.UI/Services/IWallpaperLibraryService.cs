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
    Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully cancels outstanding background tasks and awaits manifest writes before app process exits.
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Deletes an imported wallpaper by removing it from the user manifest and deleting its folder on disk.
    /// </summary>
    Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default);
}
