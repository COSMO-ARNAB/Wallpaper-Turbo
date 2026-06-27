//UpdateCoordinator.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Events;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Updater;

public sealed class UpdateCoordinator : IDisposable
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
    private ReleaseChannel _userChannel = ReleaseChannel.Stable;

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
        UpdaterDiagnostic.Log("UpdateCoordinator.ctor", "Coordinator constructed");
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
        UpdaterDiagnostic.Log("UpdateCoordinator.CheckForUpdatesAsync", $"Check requested for channel={channel}");
        _userChannel = channel;
        var newCts = new CancellationTokenSource();
        UpdateState oldState;
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState != UpdateState.Idle && 
                _currentState != UpdateState.Failed && 
                _currentState != UpdateState.UpToDate &&
                _currentState != UpdateState.UpdateAvailable)
            {
                newCts.Dispose();
                return;
            }
            
            oldState = _currentState;
            _currentState = UpdateState.Checking;
            var oldCts = _activeCts;
            _activeCts = newCts;
            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
        }
        finally
        {
            _stateLock.Release();
        }

        Debug.WriteLine($"[UpdateCoordinator] State Transition: {oldState} -> {UpdateState.Checking}");
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(oldState, UpdateState.Checking, DateTime.UtcNow));

        try
        {
            var (isAvailable, manifest) = await _updateService.CheckForUpdatesAsync(channel, _activeCts.Token);

            if (isAvailable && manifest != null)
            {
                _currentManifest = manifest;
                UpdaterDiagnostic.Log("UpdateCoordinator.CheckForUpdatesAsync", $"FINAL RESULT: IsAvailable=True, manifest={manifest.Version}");
                await ForceStateAsync(UpdateState.UpdateAvailable);
                UpdateAvailable?.Invoke(this, manifest);
            }
            else
            {
                UpdaterDiagnostic.Log("UpdateCoordinator.CheckForUpdatesAsync", $"FINAL RESULT: IsAvailable=False, transitioning to UpToDate.");
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

        var oldCts = Interlocked.Exchange(ref _activeCts, new CancellationTokenSource());
        if (oldCts != null)
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var progress = new Progress<UpdateProgress>(p => ProgressChanged?.Invoke(this, p));

        // Use a standard temp directory for the download
        string tempDir = Path.Combine(Path.GetTempPath(), "WallpaperTurboUpdates");
        Directory.CreateDirectory(tempDir);
        string destinationPath = Path.Combine(tempDir, $"WallpaperTurbo_Update_{_currentManifest.Version}.exe");

        // #region agent log
        try
        {
            var preDownloadLog = System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = "e9f6e8",
                hypothesisId = "D",
                location = "UpdateCoordinator.cs:DownloadUpdateAsync",
                message = "Pre-download destination state",
                data = new { destinationPath, fileExists = File.Exists(destinationPath) },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            File.AppendAllText(@"C:\Users\arnab\PROJECTS\Wallpaper_Turbo\debug-e9f6e8.log", preDownloadLog + Environment.NewLine);
        }
        catch { }
        // #endregion

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

        UpdaterDiagnostic.Log("UpdateCoordinator.VerifyUpdateAsync", $"Manifest signature requirement: {_currentManifest?.MinSignatureRequirement}");

        try
        {
            if (string.IsNullOrEmpty(_currentManifest?.Sha256Hash))
            {
                UpdaterDiagnostic.Log("UpdateCoordinator.VerifyUpdateAsync", $"REJECTION: empty SHA256 in manifest. State=Verifying Version={_currentManifest?.Version} Channel={_currentManifest?.Channel}");
                CleanupPartialDownload(_downloadedFilePath);
                await HandleErrorAsync("Security Validation Failed. The manifest has no SHA256 hash; integrity cannot be verified.", null);
                return;
            }

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(_downloadedFilePath);
            var hashBytes = sha256.ComputeHash(stream);
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            if (hashString != _currentManifest.Sha256Hash.ToLowerInvariant())
            {
                CleanupPartialDownload(_downloadedFilePath);
                await HandleErrorAsync("Security Validation Failed. The downloaded file hash does not match the manifest.", null);
                return;
            }

            var userMinSignatureRequirement = GitHubReleaseProvider.DefaultSignatureRequirementForChannel(_userChannel);
            if (userMinSignatureRequirement == SignatureRequirement.Authenticode &&
                _currentManifest!.MinSignatureRequirement == SignatureRequirement.Sha256Only)
            {
                UpdaterDiagnostic.Log("UpdateCoordinator.VerifyUpdateAsync", $"REJECTION: user channel {_userChannel} requires Authenticode, but manifest MinSignatureRequirement={_currentManifest.MinSignatureRequirement}. State=Verifying Version={_currentManifest.Version}");
                CleanupPartialDownload(_downloadedFilePath);
                await HandleErrorAsync("Security Validation Failed. The selected update channel requires stronger signature verification than the manifest provides.", null);
                return;
            }

            if (_currentManifest!.MinSignatureRequirement == SignatureRequirement.Sha256Only)
            {
                UpdaterDiagnostic.Log("UpdateCoordinator.VerifyUpdateAsync", $"Manifest requires Sha256Only. Skipping Authenticode. State=Verifying Version={_currentManifest.Version} Channel={_currentManifest.Channel}");
                await ForceStateAsync(UpdateState.ReadyToInstall);
                return;
            }

            bool isValid = _signatureValidator.IsValidSignature(_downloadedFilePath);
            if (isValid)
            {
                UpdaterDiagnostic.Log("UpdateCoordinator.VerifyUpdateAsync", $"Authenticode validation passed. State=Verifying Version={_currentManifest.Version} Channel={_currentManifest.Channel}");
                await ForceStateAsync(UpdateState.ReadyToInstall);
            }
            else
            {
                UpdaterDiagnostic.Log("UpdateCoordinator.VerifyUpdateAsync", $"Authenticode validation FAILED. State=Verifying Version={_currentManifest.Version} Channel={_currentManifest.Channel}");
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
            bool shutdownSuccess = await _processManager.ShutdownOtherProcessesGracefullyAsync(5000);

            // #region agent log
            try
            {
                var installLog = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId = "e9f6e8",
                    hypothesisId = "C",
                    location = "UpdateCoordinator.cs:InstallUpdateAsync",
                    message = "ShutdownOtherProcessesGracefullyAsync result",
                    data = new { shutdownSuccess },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                File.AppendAllText(@"C:\Users\arnab\PROJECTS\Wallpaper_Turbo\debug-e9f6e8.log", installLog + Environment.NewLine);
            }
            catch { }
            // #endregion
            
            if (!shutdownSuccess)
            {
                Debug.WriteLine("[UpdateCoordinator] Graceful shutdown failed. Terminating update to prevent lock errors.");
                await HandleErrorAsync("Failed to cleanly shut down the wallpaper engine.", null);
                return;
            }

            await ForceStateAsync(UpdateState.Installing);
            
            // Handoff to installer.
            _updateApplier.ApplyUpdate(_downloadedFilePath);

            // Shut down current process to release locks
            _processManager.ShutdownCurrentProcessGracefully();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCoordinator] Error during installation handoff: {ex.Message}");
            await HandleErrorAsync("Failed to launch the installer.", ex);
        }
    }

    public void CancelAsync()
    {
        var cts = _activeCts;
        if (cts != null && !cts.IsCancellationRequested)
        {
            try { cts.Cancel(); } catch { }
        }
    }

    public void Dispose()
    {
        try { _activeCts?.Cancel(); } catch { }
        _activeCts?.Dispose();
        // Do NOT dispose _stateLock (SemaphoreSlim) to prevent ObjectDisposedException 
        // for threads currently awaiting it during shutdown.
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
