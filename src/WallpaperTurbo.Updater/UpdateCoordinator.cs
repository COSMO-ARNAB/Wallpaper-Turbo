//UpdateCoordinator.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Events;

namespace WallpaperTurbo.Updater;

public sealed class UpdateCoordinator
{
    private readonly IUpdateService _updateService;
    private readonly IDownloadManager _downloadManager;
    private readonly ISignatureValidator _signatureValidator;
    private readonly IUpdateApplier _updateApplier;
    private readonly IProcessManager _processManager;

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private CancellationTokenSource? _activeCts;

    private UpdateState _currentState = UpdateState.Idle;
    private UpdateManifest? _currentManifest;
    private string? _downloadedFilePath;

    public event EventHandler<UpdateStateChangedEventArgs>? StateChanged;
    public event EventHandler<UpdateManifest>? UpdateAvailable;
    public event EventHandler<UpdateProgress>? ProgressChanged;
    public event EventHandler<UpdateErrorEventArgs>? ErrorOccurred;

    public UpdateState CurrentState => _currentState;
    public UpdateManifest? CurrentManifest => _currentManifest;

    public UpdateCoordinator(
        IUpdateService updateService,
        IDownloadManager downloadManager,
        ISignatureValidator signatureValidator,
        IUpdateApplier updateApplier,
        IProcessManager processManager)
    {
        _updateService = updateService;
        _downloadManager = downloadManager;
        _signatureValidator = signatureValidator;
        _updateApplier = updateApplier;
        _processManager = processManager;
    }

    private async Task<bool> TransitionStateAsync(UpdateState expectedCurrentState, UpdateState newState)
    {
        UpdateState oldState;
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState != expectedCurrentState && expectedCurrentState != UpdateState.Idle) // Idle is a wild-card start for some
            {
                if (expectedCurrentState != UpdateState.Failed) // Failed allows retries
                {
                    Debug.WriteLine($"[UpdateCoordinator] Invalid transition: Expected {_currentState} to be {expectedCurrentState}. Aborting transition to {newState}.");
                    return false;
                }
            }

            oldState = _currentState;
            _currentState = newState;
        }
        finally
        {
            _stateLock.Release();
        }

        Debug.WriteLine($"[UpdateCoordinator] State Transition: {oldState} -> {newState}");
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(oldState, newState, DateTime.UtcNow));
        return true;
    }

    private async Task ForceStateAsync(UpdateState newState)
    {
        UpdateState oldState;
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState == newState) return;
            oldState = _currentState;
            _currentState = newState;
        }
        finally
        {
            _stateLock.Release();
        }

        Debug.WriteLine($"[UpdateCoordinator] Forced State Transition: {oldState} -> {newState}");
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(oldState, newState, DateTime.UtcNow));
    }

    public async Task CheckForUpdatesAsync(ReleaseChannel channel)
    {
        UpdateState oldState;
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState != UpdateState.Idle && 
                _currentState != UpdateState.Failed && 
                _currentState != UpdateState.UpToDate)
            {
                return;
            }
            
            oldState = _currentState;
            _currentState = UpdateState.Checking;
        }
        finally
        {
            _stateLock.Release();
        }

        Debug.WriteLine($"[UpdateCoordinator] State Transition: {oldState} -> {UpdateState.Checking}");
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(oldState, UpdateState.Checking, DateTime.UtcNow));

        _activeCts = new CancellationTokenSource();

        try
        {
            var (isAvailable, manifest) = await _updateService.CheckForUpdatesAsync(channel, _activeCts.Token);
            
            if (isAvailable && manifest != null)
            {
                _currentManifest = manifest;
                await ForceStateAsync(UpdateState.UpdateAvailable);
                UpdateAvailable?.Invoke(this, manifest);
            }
            else
            {
                await ForceStateAsync(UpdateState.UpToDate);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[UpdateCoordinator] CheckForUpdatesAsync was cancelled.");
            await ForceStateAsync(UpdateState.Idle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCoordinator] Error during check: {ex.Message}");
            await HandleErrorAsync("Failed to check for updates.", ex);
        }
    }

    public async Task DownloadUpdateAsync()
    {
        if (!await TransitionStateAsync(UpdateState.UpdateAvailable, UpdateState.Downloading))
            return;

        if (_currentManifest == null)
        {
            await HandleErrorAsync("Cannot download: Manifest is null.", null);
            return;
        }

        _activeCts = new CancellationTokenSource();
        var progress = new Progress<UpdateProgress>(p => ProgressChanged?.Invoke(this, p));

        // Use a standard temp directory for the download
        string tempDir = Path.Combine(Path.GetTempPath(), "WallpaperTurboUpdates");
        Directory.CreateDirectory(tempDir);
        string destinationPath = Path.Combine(tempDir, $"WallpaperTurbo_Update_{_currentManifest.Version}.exe");

        try
        {
            _downloadedFilePath = await _downloadManager.DownloadUpdateAsync(_currentManifest, destinationPath, progress, _activeCts.Token);
            
            await ForceStateAsync(UpdateState.Downloaded);
            
            // Automatic transition to Verification
            await VerifyUpdateAsync();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[UpdateCoordinator] Download was cancelled.");
            CleanupPartialDownload(destinationPath);
            await ForceStateAsync(UpdateState.UpdateAvailable);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCoordinator] Error during download: {ex.Message}");
            CleanupPartialDownload(destinationPath);
            await HandleErrorAsync("Failed to download update.", ex);
        }
    }

    private async Task VerifyUpdateAsync()
    {
        if (!await TransitionStateAsync(UpdateState.Downloaded, UpdateState.Verifying))
            return;

        if (string.IsNullOrEmpty(_downloadedFilePath) || !File.Exists(_downloadedFilePath))
        {
            await HandleErrorAsync("Cannot verify: Downloaded file is missing.", null);
            return;
        }

        try
        {
            // Note: If running on an OS/environment where Authenticode throws immediately,
            // we catch it. Usually IsValidSignature returns a boolean.
            bool isValid = _signatureValidator.IsValidSignature(_downloadedFilePath);

            if (isValid)
            {
                await ForceStateAsync(UpdateState.ReadyToInstall);
            }
            else
            {
                CleanupPartialDownload(_downloadedFilePath);
                await HandleErrorAsync("Security Validation Failed. The downloaded file signature is invalid.", null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCoordinator] Error during verification: {ex.Message}");
            CleanupPartialDownload(_downloadedFilePath);
            await HandleErrorAsync("Security Validation Exception.", ex);
        }
    }

    public async Task InstallUpdateAsync()
    {
        if (!await TransitionStateAsync(UpdateState.ReadyToInstall, UpdateState.ShuttingDown))
            return;

        if (string.IsNullOrEmpty(_downloadedFilePath) || !File.Exists(_downloadedFilePath))
        {
            await HandleErrorAsync("Cannot install: Downloaded file is missing.", null);
            return;
        }

        try
        {
            bool shutdownSuccess = await _processManager.ShutdownAppRunnerGracefullyAsync(5000);
            
            if (!shutdownSuccess)
            {
                Debug.WriteLine("[UpdateCoordinator] Graceful shutdown failed. Terminating update to prevent lock errors.");
                await HandleErrorAsync("Failed to cleanly shut down the wallpaper engine.", null);
                return;
            }

            await ForceStateAsync(UpdateState.Installing);
            
            // Handoff to installer.
            // This is terminal for this process instance.
            _updateApplier.ApplyUpdate(_downloadedFilePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCoordinator] Error during installation handoff: {ex.Message}");
            await HandleErrorAsync("Failed to launch the installer.", ex);
        }
    }

    public void CancelAsync()
    {
        _activeCts?.Cancel();
    }

    private async Task HandleErrorAsync(string message, Exception? ex)
    {
        UpdateState stateWhenFailed = _currentState;
        await ForceStateAsync(UpdateState.Failed);
        ErrorOccurred?.Invoke(this, new UpdateErrorEventArgs(message, ex, stateWhenFailed));
    }

    private void CleanupPartialDownload(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.WriteLine($"[UpdateCoordinator] Cleaned up file: {path}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCoordinator] Failed to clean up file {path}: {ex.Message}");
        }
    }
}
