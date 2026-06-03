using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface IUpdateSourceProvider
{
    Task<UpdateManifest?> GetLatestManifestAsync(ReleaseChannel channel, CancellationToken cancellationToken = default);
}
