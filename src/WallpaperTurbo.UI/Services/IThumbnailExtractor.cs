using System.Threading;
using System.Threading.Tasks;

namespace WallpaperTurbo.UI.Services;

/// <summary>
/// Defines a renderer-agnostic contract for generating scaled thumbnail images from media assets.
/// </summary>
public interface IThumbnailExtractor
{
    /// <summary>
    /// Extracts a representative thumbnail from the specified media source and saves it to the output directory.
    /// </summary>
    /// <param name="mediaPath">The absolute path of the source video or image file.</param>
    /// <param name="outputDirectory">The absolute path where the thumbnail.jpg should be saved.</param>
    /// <param name="cancellationToken">The coordinated token to monitor for cancellations.</param>
    /// <returns>The absolute file path of the successfully cached thumbnail.jpg.</returns>
    Task<string> ExtractThumbnailAsync(string mediaPath, string outputDirectory, CancellationToken cancellationToken);
}
