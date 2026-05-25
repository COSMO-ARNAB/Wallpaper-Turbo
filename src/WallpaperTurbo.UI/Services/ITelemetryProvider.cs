using System;

namespace WallpaperTurbo.UI.Services;

public interface ITelemetryProvider : IDisposable
{
    bool IsSupported { get; }
    bool Initialize(int pid);
    void Poll(int pid, TelemetryMetrics metrics);
    void Reset();
}
