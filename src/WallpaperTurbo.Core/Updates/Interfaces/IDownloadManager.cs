using System;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface IDownloadManager
{
    Task<string> DownloadUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);
}
