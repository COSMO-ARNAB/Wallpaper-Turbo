using System.Threading.Tasks;

namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface IProcessManager
{
    Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds);
    void ShutdownCurrentProcessGracefully();
}
