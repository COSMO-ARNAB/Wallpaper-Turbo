using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Updater.Services;

public sealed class HttpDownloadManager : IDownloadManager, IDisposable
{
    private readonly HttpClient _httpClient;
    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMs = 1000;
    private bool _disposed;

    public HttpDownloadManager(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateManifest manifest,
        string destinationPath,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrEmpty(destinationPath)) throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationPath));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                await DownloadWithProgressAsync(manifest, destinationPath, progress, cancellationToken);
                return destinationPath;
            }
            catch (OperationCanceledException)
            {
                // Keep the partially downloaded file so the next attempt can resume.
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < MaxRetryAttempts)
                {
                    Debug.WriteLine($"[HttpDownloadManager] Download attempt {attempt} failed: {ex.Message}. Retrying...");
                    await Task.Delay(RetryDelayMs * attempt, cancellationToken);
                }
            }
        }

        Debug.WriteLine($"[HttpDownloadManager] All download attempts failed.");
        // Keep the partially downloaded file so a future download attempt can resume.
        throw new InvalidOperationException($"Failed to download update after {MaxRetryAttempts} attempts.", lastException);
    }

    private async Task DownloadWithProgressAsync(
        UpdateManifest manifest,
        string destinationPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken,
        bool allowResume = true)
    {
        // Resume support: if a partial file already exists, request only the remainder.
        long existingBytes = (allowResume && File.Exists(destinationPath)) ? new FileInfo(destinationPath).Length : 0;

        // Already fully downloaded — skip the network and let verification validate the file.
        // Strict equality: an oversized partial goes through the normal request path so a
        // 416 (or a 200 that truncates) heals it, instead of punting to verification.
        if (existingBytes > 0 && manifest.FileSizeBytes > 0 && existingBytes == manifest.FileSizeBytes)
        {
            progress?.Report(new UpdateProgress(existingBytes, existingBytes, 100));
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.DownloadUrl);
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // The partial file is stale (e.g. the release asset was re-published smaller
        // under the same URL): the requested offset is past the end of the content.
        // Retrying the Range would 416 forever, so drop the partial and restart with resume disabled.
        if (allowResume && existingBytes > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            Debug.WriteLine($"[HttpDownloadManager] Server returned 416 for Range from byte {existingBytes}. Discarding stale partial file and restarting download.");
            TryDeleteFile(destinationPath);
            await DownloadWithProgressAsync(manifest, destinationPath, progress, cancellationToken, allowResume: false);
            return;
        }

        response.EnsureSuccessStatusCode();

        // Server honored the Range request (206) -> append; otherwise (200) start over.
        bool isResumed = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        long offset = isResumed ? existingBytes : 0;

        long totalBytes = response.Content.Headers.ContentLength.HasValue
            ? response.Content.Headers.ContentLength.Value + offset
            : manifest.FileSizeBytes;

        var content = response.Content;

        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(
            destinationPath,
            isResumed ? FileMode.OpenOrCreate : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        if (isResumed)
        {
            fileStream.Seek(offset, SeekOrigin.Begin);
        }

        var buffer = new byte[81920];
        long downloadedBytes = offset;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            double percent = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100.0 : 0;
            var updateProgress = new UpdateProgress(downloadedBytes, totalBytes, percent);
            progress?.Report(updateProgress);
        }

        await fileStream.FlushAsync(cancellationToken);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HttpDownloadManager] Failed to delete file {path}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
