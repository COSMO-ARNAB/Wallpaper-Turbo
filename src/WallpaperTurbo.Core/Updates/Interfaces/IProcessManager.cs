using System.Threading.Tasks;

namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface IProcessManager
{
    Task<bool> ShutdownAppRunnerGracefullyAsync(int timeoutMilliseconds);
}
