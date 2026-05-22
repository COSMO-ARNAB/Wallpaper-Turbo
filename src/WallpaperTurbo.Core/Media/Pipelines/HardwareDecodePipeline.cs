// HardwareDecodePipeline.cs

using System;
using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
using WallpaperTurbo.Core.Media;
using WallpaperTurbo.Core.Models;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Media.Pipelines;

/// <summary>
/// Hardware-accelerated decode pipeline backed by LibVLC.
/// Tuned for embedded desktop wallpaper rendering.
/// </summary>
public sealed class HardwareDecodePipeline
    : IMediaPipeline
{
    private readonly VideoDecodeMode _decodeMode;

    private readonly string? _videoOutputModule;

    private LibVLC? _libVLC;

    private MediaPlayer? _mediaPlayer;

    private LibVLCSharp.Shared.Media? _media;

    private IntPtr _parentWindowHandle = IntPtr.Zero;

    private long _suspendedTime = -1;

    private readonly object _sync =
        new();

    public PipelineType Type =>
        PipelineType.HardwareDecode;

    public bool SuspendAsPause { get; set; } = true;

    public int FileCachingMs { get; set; } = 1000;

    public HardwareDecodePipeline(
        VideoDecodeMode decodeMode = VideoDecodeMode.Auto,
        string? videoOutputModule = null,
        bool suspendAsPause = true,
        int fileCachingMs = 1000)
    {
        _decodeMode = decodeMode;
        _videoOutputModule = string.IsNullOrWhiteSpace(videoOutputModule)
            ? null
            : videoOutputModule.Trim();
        SuspendAsPause = suspendAsPause;
        FileCachingMs = fileCachingMs;
    }

    public void Initialize(
        IntPtr parentWindowHandle)
    {
        lock (_sync)
        {
            if (_libVLC != null)
                return;

            _parentWindowHandle = parentWindowHandle;

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
            var argsList = new System.Collections.Generic.List<string>
            {
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
                "--loop",

                //
                // Native Memory Buffering Reductions (Universal)
                //
                $"--file-caching={FileCachingMs}",
                $"--network-caching={FileCachingMs}",
                $"--live-caching={FileCachingMs}",
                $"--disc-caching={FileCachingMs}",
                "--no-stats",
                "--no-sub-autodetect-file",
                "--no-snapshot-preview"
            };

            if (_videoOutputModule != null)
            {
                argsList.Add($"--vout={_videoOutputModule}");
            }

            if (_decodeMode == VideoDecodeMode.Software)
            {
                argsList.AddRange(new[]
                {
                    "--avcodec-hw=none",
                    "--avcodec-threads=1"
                });
            }
            else if (_decodeMode == VideoDecodeMode.Hardware)
            {
                argsList.Add("--avcodec-hw=d3d11va");
            }

#if DEBUG
            argsList.Add("--verbose=2");
#endif

            string[] args = argsList.ToArray();

            _libVLC =
                new LibVLC(args);

#if DEBUG
            _libVLC.Log += (s, ev) =>
            {
                System.Diagnostics.Debug.WriteLine($"[VLC] {ev.Level}: {ev.Message} ({ev.Module})");
            };
#endif

            _mediaPlayer =
                new MediaPlayer(_libVLC)
                {
                    EnableHardwareDecoding = _decodeMode != VideoDecodeMode.Software
                };

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

            try
            {
                _mediaPlayer.Stop();
            }
            catch
            {
            }

            _media?.Dispose();

            _media =
                new LibVLCSharp.Shared.Media(
                    _libVLC,
                    filePath,
                    FromType.FromPath);

            if (_decodeMode == VideoDecodeMode.Software)
            {
                _media.AddOption(":avcodec-hw=none");
            }

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

    public void Suspend()
    {
        lock (_sync)
        {
            if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
            {
                if (SuspendAsPause)
                {
                    _mediaPlayer.Pause();
                    Console.WriteLine("[Pipeline] Paused playback (suspend-as-pause mode) to keep D3D11 active and ensure buttery smooth resumption.");
                }
                else
                {
                    _suspendedTime = _mediaPlayer.Time;
                    _mediaPlayer.Stop();
                    Console.WriteLine($"[Pipeline] Suspended playback at {_suspendedTime}ms (stop mode) to reclaim system and GPU resources.");
                }
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_mediaPlayer != null)
            {
                if (SuspendAsPause)
                {
                    _mediaPlayer.Play();
                    Console.WriteLine("[Pipeline] Resumed playback instantly from paused state.");
                }
                else
                {
                    if (_suspendedTime >= 0)
                    {
                        long targetTime = _suspendedTime;
                        _suspendedTime = -1;

                        if (_libVLC != null && _media != null)
                        {
                            string mrl = _media.Mrl;
                            _media.Dispose();

                            _media = new LibVLCSharp.Shared.Media(_libVLC, mrl, FromType.FromLocation);
                            
                            if (_decodeMode == VideoDecodeMode.Software)
                            {
                                _media.AddOption(":avcodec-hw=none");
                            }
                            _media.AddOption(":embedded-video");
                            _media.AddOption(":no-fullscreen");
                            _media.AddOption(":input-repeat=65535");
                            _media.AddOption(":no-video-title-show");
                            
                            // Convert ms to seconds double (e.g. 1500ms -> 1.5s)
                            double seconds = targetTime / 1000.0;
                            _media.AddOption($":start-time={seconds:0.###}");
                            
                            _mediaPlayer.Media = _media;
                            Console.WriteLine($"[Pipeline] Resumed with optimized start-time: {seconds:0.###}s");
                        }
                    }
                    
                    _mediaPlayer.Play();
                }

                // Make all dynamically spawned media player child windows click-through immediately on resume
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(300);
                    lock (_sync)
                    {
                        if (_mediaPlayer != null && _mediaPlayer.Hwnd != IntPtr.Zero)
                        {
                            WallpaperTurbo.Core.Interop.WindowUtil.MakeChildrenTransparent(_mediaPlayer.Hwnd);
                        }
                    }
                });
            }
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

    public void ApplyLayoutMode(WallpaperLayoutMode mode)
    {
        lock (_sync)
        {
            if (_mediaPlayer == null)
                return;

            int width = 1920;
            int height = 1080;
            if (_parentWindowHandle != IntPtr.Zero && NativeMethods.GetClientRect(_parentWindowHandle, out var rect))
            {
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w > 0 && h > 0)
                {
                    width = w;
                    height = h;
                }
            }

            try
            {
                // Explicitly reset AspectRatio and CropGeometry to null to flush LibVLC's scaling cache
                _mediaPlayer.AspectRatio = null;
                _mediaPlayer.CropGeometry = null;

                switch (mode)
                {
                    case WallpaperLayoutMode.Stretch:
                        _mediaPlayer.AspectRatio = $"{width}:{height}";
                        break;
                    case WallpaperLayoutMode.Fit:
                        break;
                    case WallpaperLayoutMode.Fill:
                        _mediaPlayer.CropGeometry = $"{width}:{height}";
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Failed to apply layout mode {mode}: {ex.Message}");
            }
        }
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
