//IMediaPipeline.cs - Interface for media pipelines in Wallpaper Turbo.
using WallpaperTurbo.Core.Models;

namespace WallpaperTurbo.Core.Media;

public interface IMediaPipeline
{
    PipelineType Type { get; }

    void Initialize(IntPtr parentWindowHandle);

    /// <summary>
    /// Pre-opens the media file in the background so the next <see cref="LoadMedia"/> call
    /// can assign an already-parsed Media object instead of re-opening from disk.
    /// Safe to call while another media is playing. No-op if not supported.
    /// </summary>
    void PreloadMedia(string filePath);

    void LoadMedia(string filePath);

    void Play();

    void Pause();

    void Suspend();

    void Resume();

    void SetTargetFps(int fps);

    void ApplyLayoutMode(WallpaperLayoutMode mode);

    void SetMute(bool mute);

    void Release();
}