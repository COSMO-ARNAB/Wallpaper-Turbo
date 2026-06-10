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
    private readonly bool _useSoftwareDecode;

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

    private bool _startMuted = true;

    public HardwareDecodePipeline(
        bool useSoftwareDecode = false,
        string? videoOutputModule = null,
        bool startMuted = true)
    {
        _useSoftwareDecode = useSoftwareDecode;
        _videoOutputModule = string.IsNullOrWhiteSpace(videoOutputModule)
            ? null
            : videoOutputModule.Trim();
        _startMuted = startMuted;
    }

    public void Initialize(
        IntPtr parentWindowHandle)
    {
        lock (_sync)
        {
            if (_libVLC != null)
                return;

            _parentWindowHandle = parentWindowHandle;

            string vlcPath = Path.Combine(
                AppContext.BaseDirectory,
                "libvlc",
                "win-x64");

            Console.WriteLine($"VLC Path: {vlcPath}");
            Console.WriteLine($"Exists: {Directory.Exists(vlcPath)}");

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
                // Hardware decoding.
                //
                _useSoftwareDecode
                    ? "--avcodec-hw=none"
                    : "--avcodec-hw=d3d11va",

                _videoOutputModule == null
                    ? "--vout=direct3d11"
                    : $"--vout={_videoOutputModule}",

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
                // "--qt-minimal-view",

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

            if (_useSoftwareDecode)
            {
                argsList.AddRange(new[]
                {
                    "--avcodec-threads=1",
                    "--file-caching=200",
                    "--network-caching=200",
                    "--live-caching=200",
                    "--disc-caching=200",
                    "--no-stats",
                    "--no-sub-autodetect-file",
                    "--no-snapshot-preview"
                });
            }

            string[] args = argsList.ToArray();

            _libVLC =
                new LibVLC(args);

            _mediaPlayer =
                new MediaPlayer(_libVLC)
                {
                    EnableHardwareDecoding = !_useSoftwareDecode
                };
            _mediaPlayer.Mute = _startMuted;

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

            if (_useSoftwareDecode)
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
                _suspendedTime = _mediaPlayer.Time;
                _mediaPlayer.Stop();
                Console.WriteLine($"[Pipeline] Suspended playback at {_suspendedTime}ms to reclaim system and GPU resources.");
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Play();
                if (_suspendedTime >= 0)
                {
                    long targetTime = _suspendedTime;
                    _suspendedTime = -1;

                    // Set time after a brief delay to ensure VLC has opened the media and decoder is active
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        for (int i = 0; i < 20; i++) // Try up to 2 seconds (20 * 100ms)
                        {
                            await System.Threading.Tasks.Task.Delay(100);
                            lock (_sync)
                            {
                                if (_mediaPlayer == null) break;
                                // If the media player is actively playing or reports a valid time, apply the seek
                                if (_mediaPlayer.IsPlaying || _mediaPlayer.Time > 0)
                                {
                                    _mediaPlayer.Time = targetTime;
                                    Console.WriteLine($"[Pipeline] Resumed and seeked to {targetTime}ms successfully.");
                                    break;
                                }
                            }
                        }
                    });
                }
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

    public void SetMute(bool mute)
    {
        lock (_sync)
        {
            _startMuted = mute;
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Mute = mute;
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
