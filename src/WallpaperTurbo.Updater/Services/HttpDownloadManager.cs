using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Updater.Services;

public sealed class HttpDownloadManager : IDownloadManager
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

        if (File.Exists(destinationPath))
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpDownloadManager] Could not delete existing file {destinationPath}: {ex.Message}");
            }
        }

        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                await DownloadWithProgressAsync(manifest, destinationPath, progress, cancellationToken);
                return destinationPath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CleanupFile(destinationPath);
                throw;
            }
            catch (OperationCanceledException)
            {
                CleanupFile(destinationPath);
                throw;
            }
            catch (Exception ex) when (attempt < MaxRetryAttempts)
            {
                lastException = ex;
                Debug.WriteLine($"[HttpDownloadManager] Download attempt {attempt} failed: {ex.Message}. Retrying...");
                await Task.Delay(RetryDelayMs * attempt, cancellationToken);
            }
        }

        Debug.WriteLine($"[HttpDownloadManager] All download attempts failed.");
        CleanupFile(destinationPath);
        throw new InvalidOperationException($"Failed to download update after {MaxRetryAttempts} attempts.", lastException);
    }

    private async Task DownloadWithProgressAsync(
        UpdateManifest manifest,
        string destinationPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? manifest.FileSizeBytes;
        var content = response.Content;

        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long downloadedBytes = 0;
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

    private static void CleanupFile(string path)
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
            Debug.WriteLine($"[HttpDownloadManager] Failed to cleanup file: {ex.Message}");
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