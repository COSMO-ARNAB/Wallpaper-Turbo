//IMediaPipeline.cs - Interface for media pipelines in Wallpaper Turbo.
using WallpaperTurbo.Core.Models;

namespace WallpaperTurbo.Core.Media;

public interface IMediaPipeline
{
    PipelineType Type { get; }

    void Initialize(IntPtr parentWindowHandle);

    void LoadMedia(string filePath);

    void Play();

    void Pause();

    void Suspend();

    void Resume();

    void SetTargetFps(int fps);

    void ApplyLayoutMode(WallpaperLayoutMode mode);

    void Release();
}