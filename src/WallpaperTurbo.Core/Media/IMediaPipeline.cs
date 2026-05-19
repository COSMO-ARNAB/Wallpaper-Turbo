//IMediaPipeline.cs - Interface for media pipelines in Wallpaper Turbo.
namespace WallpaperTurbo.Core.Media;

public interface IMediaPipeline
{
    PipelineType Type { get; }

    void Initialize(IntPtr parentWindowHandle);

    void LoadMedia(string filePath);

    void Play();

    void Pause();

    void SetTargetFps(int fps);

    void Release();
}