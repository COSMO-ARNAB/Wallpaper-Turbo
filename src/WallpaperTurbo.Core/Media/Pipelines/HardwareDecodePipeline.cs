// HardwareDecodePipeline.cs

using System;
using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
using WallpaperTurbo.Core.Media;

namespace WallpaperTurbo.Core.Media.Pipelines;

/// <summary>
/// Hardware-accelerated decode pipeline backed by LibVLC.
/// Tuned for embedded desktop wallpaper rendering.
/// </summary>
public sealed class HardwareDecodePipeline
    : IMediaPipeline
{
    private LibVLC? _libVLC;

    private MediaPlayer? _mediaPlayer;

    private LibVLCSharp.Shared.Media? _media;

    private readonly object _sync =
        new();

    public PipelineType Type =>
        PipelineType.HardwareDecode;

    public void Initialize(
        IntPtr parentWindowHandle)
    {
        lock (_sync)
        {
            if (_libVLC != null)
                return;

            string vlcPath =
                @"C:\Program Files\VideoLAN\VLC";

            if (!Directory.Exists(vlcPath))
            {
                throw new DirectoryNotFoundException(
                    $"VLC not found: {vlcPath}");
            }

            //
            // Use installed VLC runtime.
            //
            LibVLCSharp.Shared.Core.Initialize(vlcPath);

            //
            // IMPORTANT:
            // Embedded wallpaper-safe VLC flags.
            //
            string[] args =
            {
                //
                // Hardware decoding.
                //
                "--avcodec-hw=d3d11va",

                //
                // No audio path.
                //
                "--no-audio",

                //
                // Prevent fullscreen promotion.
                //
                "--no-fullscreen",

                //
                // Embedded child rendering only.
                //
                "--embedded-video",

                //
                // Cleaner desktop rendering.
                //
                "--no-video-deco",
                "--no-osd",
                "--no-video-title-show",

                //
                // Stability.
                //
                "--disable-screensaver",
                "--input-fast-seek",

                //
                // Avoid Qt activation behavior.
                //
                "--qt-minimal-view",

                //
                // Reduce unwanted UI hooks.
                //
                "--intf=dummy",
                "--no-mouse-events",
                "--no-keyboard-events",

                //
                // Continuous playback.
                //
                "--loop"
            };

            _libVLC =
                new LibVLC(args);

            _mediaPlayer =
                new MediaPlayer(_libVLC);

            //
            // CRITICAL:
            // Bind VLC directly into our
            // desktop-hosted render window.
            //
            _mediaPlayer.Hwnd =
                parentWindowHandle;

            //
            // Defensive:
            // never allow fullscreen promotion.
            //
            _mediaPlayer.Fullscreen =
                false;
        }
    }

    public void LoadMedia(
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "Invalid media path.",
                nameof(filePath));
        }

        lock (_sync)
        {
            if (_libVLC == null ||
                _mediaPlayer == null)
            {
                throw new InvalidOperationException(
                    "Pipeline not initialized.");
            }

            _media?.Dispose();

            _media =
                new LibVLCSharp.Shared.Media(
                    _libVLC,
                    filePath,
                    FromType.FromPath);

            //
            // Embedded playback only.
            //
            _media.AddOption(":embedded-video");

            //
            // Never allow fullscreen.
            //
            _media.AddOption(":no-fullscreen");

            //
            // Loop forever.
            //
            _media.AddOption(":input-repeat=65535");

            //
            // Reduce compositor disruptions.
            //
            _media.AddOption(":no-video-title-show");

            _mediaPlayer.Media =
                _media;
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            if (_mediaPlayer == null)
            {
                throw new InvalidOperationException(
                    "MediaPlayer not initialized.");
            }

            _mediaPlayer.Play();

            // Wait for VLC to spawn its child windows and make them transparent
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(500);
                if (_mediaPlayer != null && _mediaPlayer.Hwnd != IntPtr.Zero)
                {
                    WallpaperTurbo.Core.Interop.WindowUtil.MakeChildrenTransparent(_mediaPlayer.Hwnd);
                }
            });
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            _mediaPlayer?.Pause();
        }
    }

    public void SetTargetFps(
        int fps)
    {
        //
        // VLC internally manages timing.
        // Reserved for future renderer tuning.
        //
        Trace.TraceInformation(
            $"SetTargetFps({fps})");
    }

    public void Release()
    {
        lock (_sync)
        {
            try
            {
                _mediaPlayer?.Stop();
            }
            catch
            {
            }

            _mediaPlayer?.Dispose();
            _media?.Dispose();
            _libVLC?.Dispose();

            _mediaPlayer =
                null;

            _media =
                null;

            _libVLC =
                null;
        }
    }
}