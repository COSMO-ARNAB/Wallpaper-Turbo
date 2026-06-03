using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface IUpdateService
{
    Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(ReleaseChannel channel, CancellationToken cancellationToken = default);
}
