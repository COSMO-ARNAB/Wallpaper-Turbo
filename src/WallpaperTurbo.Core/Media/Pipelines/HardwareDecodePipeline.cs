using System;
using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
using WallpaperTurbo.Core.Media;

namespace WallpaperTurbo.Core.Media.Pipelines
{
    /// <summary>
    /// Hardware-accelerated decode pipeline backed by LibVLC.
    /// </summary>
    public sealed class HardwareDecodePipeline : IMediaPipeline
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private LibVLCSharp.Shared.Media? _media;
        private readonly object _sync = new();

        public PipelineType Type => PipelineType.HardwareDecode;

        public void Initialize(IntPtr parentWindowHandle)
        {
            lock (_sync)
            {
                if (_libVLC != null)
                    return;

                // 1. Point directly to your computer's native VLC installation!
                string vlcPath = @"C:\Program Files\VideoLAN\VLC";
                
                if (!Directory.Exists(vlcPath))
                {
                    throw new DirectoryNotFoundException($"VLC is not installed at {vlcPath}. Please install VLC Media Player on your PC!");
                }

                // Tell LibVLCSharp to use the local files instead of NuGet binaries
                LibVLCSharp.Shared.Core.Initialize(vlcPath);

                // 2. Use the CORRECT VLC hardware acceleration flag
                var args = new[]
                {
                    "--avcodec-hw=none", // Disable hardware acceleration for maximum compatibility. Change to "dxva2" or "d3d11va" if you want to experiment with GPU decoding.
                    "--no-audio",
                    "--no-osd",
                    "--input-fast-seek",
                    "--no-video-title-show"
                };

                _libVLC = new LibVLC(args);
                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);

                // Bind to the provided native window handle for video output.
                _mediaPlayer.Hwnd = parentWindowHandle;
            }
        }

        public void LoadMedia(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath must be a valid path", nameof(filePath));

            lock (_sync)
            {
                if (_libVLC == null || _mediaPlayer == null)
                    throw new InvalidOperationException("Pipeline not initialized. Call Initialize before loading media.");

                _media?.Dispose();
                _media = new LibVLCSharp.Shared.Media(_libVLC, filePath, FromType.FromPath);
                
                // Force continuous loop
                _media.AddOption(":input-repeat=65535");
                
                _mediaPlayer.Media = _media;
            }
        }

        public void Play()
        {
            lock (_sync)
            {
                if (_mediaPlayer == null)
                    throw new InvalidOperationException("MediaPlayer is not initialized.");

                _mediaPlayer.Play();
            }
        }

        public void Pause()
        {
            lock (_sync)
            {
                _mediaPlayer?.Pause();
            }
        }

        public void SetTargetFps(int fps)
        {
            // LibVLC handles playback timing internally; expose the call for future tuning.
            Trace.TraceInformation($"HardwareDecodePipeline.SetTargetFps called with {fps}");
        }

        public void Release()
        {
            lock (_sync)
            {
                try { _mediaPlayer?.Stop(); } catch { }
                _mediaPlayer?.Dispose();
                _media?.Dispose();
                _libVLC?.Dispose();

                _mediaPlayer = null;
                _media = null;
                _libVLC = null;
            }
        }
    }
}