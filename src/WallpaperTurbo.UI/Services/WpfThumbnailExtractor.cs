using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WallpaperTurbo.UI.Services;

public class WpfThumbnailExtractor : IThumbnailExtractor
{
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mkv", ".gif" };

    public Task<string> ExtractThumbnailAsync(string mediaPath, string outputDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return Task.FromException<string>(new FileNotFoundException($"Source media not found: {mediaPath}"));
        }

        string ext = Path.GetExtension(mediaPath).ToLowerInvariant();
        bool isVideo = Array.Exists(VideoExtensions, e => e == ext);

        if (isVideo)
        {
            return ExtractVideoFrameAsync(mediaPath, outputDirectory, cancellationToken);
        }
        else
        {
            return ExtractImageFrameAsync(mediaPath, outputDirectory, cancellationToken);
        }
    }

    private async Task<string> ExtractVideoFrameAsync(string mediaPath, string outputDirectory, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // We need a dedicated STA thread with its OWN Dispatcher for MediaPlayer.
        // Using Dispatcher.PushFrame is fragile: if MediaOpened fires before PushFrame
        // Correct pattern: Dispatcher.Run() + Dispatcher.CurrentDispatcher.InvokeShutdown().
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            MediaPlayer? mediaPlayer = null;

            void ShutdownDispatcher()
            {
                try
                {
                    if (mediaPlayer != null)
                    {
                        mediaPlayer.Close();
                        mediaPlayer = null;
                    }
                }
                catch { }
                // InvokeShutdown posts a shutdown to this dispatcher's queue.
                // If already shut down, this is a no-op.
                try { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                catch { }
            }

            using var registration = localCts.Token.Register(() =>
            {
                tcs.TrySetCanceled();
                dispatcher.BeginInvoke(ShutdownDispatcher, DispatcherPriority.Normal);
            });

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Thumbnail Gen] Video extraction starting: '{mediaPath}'");
                mediaPlayer = new MediaPlayer();
                mediaPlayer.Volume = 0;
                mediaPlayer.ScrubbingEnabled = true;

                mediaPlayer.MediaOpened += async (s, e) =>
                {
                    try
                    {
                        localCts.Token.ThrowIfCancellationRequested();

                        var duration = mediaPlayer.NaturalDuration.HasTimeSpan
                            ? mediaPlayer.NaturalDuration.TimeSpan
                            : TimeSpan.FromSeconds(5);
                        var targetPos = duration > TimeSpan.FromSeconds(4)
                            ? TimeSpan.FromSeconds(2)
                            : TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.1);

                        mediaPlayer.Position = targetPos;

                        // Give Media Foundation time to decode the frame at the seek position
                        await Task.Delay(800, localCts.Token);

                        localCts.Token.ThrowIfCancellationRequested();

                        int width = mediaPlayer.NaturalVideoWidth;
                        int height = mediaPlayer.NaturalVideoHeight;
                        if (width <= 0 || height <= 0) { width = 1920; height = 1080; }

                        // Pre-scale to 1280px width for sharp Hero/card use
                        int thumbWidth = 1280;
                        int thumbHeight = (int)((double)height / width * thumbWidth);
                        if (thumbHeight <= 0) thumbHeight = 720;

                        var scaleVisual = new DrawingVisual();
                        using (var ctx = scaleVisual.RenderOpen())
                        {
                            ctx.PushTransform(new ScaleTransform((double)thumbWidth / width, (double)thumbHeight / height));
                            ctx.DrawVideo(mediaPlayer, new Rect(0, 0, width, height));
                        }

                        var renderTarget = new RenderTargetBitmap(thumbWidth, thumbHeight, 96, 96, PixelFormats.Pbgra32);
                        renderTarget.Render(scaleVisual);

                        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

                        string outputPath = Path.Combine(outputDirectory, "thumbnail.jpg");
                        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            encoder.Save(fs);
                        }

                        tcs.TrySetResult(outputPath);
                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Success] Saved '{outputPath}' ({new FileInfo(outputPath).Length} bytes)");
                    }
                    catch (OperationCanceledException)
                    {
                        tcs.TrySetCanceled();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] MediaOpened error for '{mediaPath}': {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                    finally
                    {
                        ShutdownDispatcher();
                    }
                };

                mediaPlayer.MediaFailed += (s, e) =>
                {
                    var errorMsg = e.ErrorException?.Message ?? "Media Foundation failed to open source.";
                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] MediaFailed '{mediaPath}': {errorMsg}");
                    tcs.TrySetException(e.ErrorException ?? new Exception(errorMsg));
                    ShutdownDispatcher();
                };

                mediaPlayer.Open(new Uri(mediaPath));

                // Dispatcher.Run() is the correct way to pump a non-main-thread Dispatcher.
                // It blocks until InvokeShutdown() is called (which ShutdownDispatcher does).
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                try { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } catch { }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = $"ThumbnailSTA_{Path.GetFileNameWithoutExtension(mediaPath)}";
        thread.Start();

        // 6-second watchdog using Task.WhenAny - never blocks UI thread
        using var wdCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watchdog = Task.Delay(6000, wdCts.Token);
        var completed = await Task.WhenAny(tcs.Task, watchdog);

        if (completed == tcs.Task)
        {
            wdCts.Cancel(); // Cancel the watchdog timer
            return await tcs.Task;
        }

        // On timeout, cleanly shut down the STA thread by triggering localCts
        localCts.Cancel();

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.TrySetCanceled();
            throw new OperationCanceledException(cancellationToken);
        }

        tcs.TrySetException(new TimeoutException($"Video thumbnail extraction exceeded 6000ms for '{mediaPath}'."));
        throw new TimeoutException($"Video thumbnail extraction exceeded 6000ms for '{mediaPath}'.");
    }

    private Task<string> ExtractImageFrameAsync(string mediaPath, string outputDirectory, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Thumbnail Gen] Image extraction: '{mediaPath}'");
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = new FileStream(mediaPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];

                int width = frame.PixelWidth;
                int height = frame.PixelHeight;

                int thumbWidth = 1280;
                int thumbHeight = (int)((double)height / width * thumbWidth);
                if (thumbHeight <= 0) thumbHeight = 720;

                var scale = new ScaleTransform((double)thumbWidth / width, (double)thumbHeight / height);
                var scaledBitmap = new TransformedBitmap(frame, scale);

                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(scaledBitmap));

                string outputPath = Path.Combine(outputDirectory, "thumbnail.jpg");
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(fs);
                }

                System.Diagnostics.Debug.WriteLine($"[Thumbnail Success] Image '{outputPath}' ({new FileInfo(outputPath).Length} bytes)");
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] Image extraction failed '{mediaPath}': {ex.Message}");
                throw new Exception($"Image thumbnail generation failed: {ex.Message}", ex);
            }
        }, cancellationToken);
    }
}
