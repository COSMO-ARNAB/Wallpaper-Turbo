using System;
using System.Diagnostics;
using WallpaperTurbo.Core.Updates.Interfaces;

namespace WallpaperTurbo.Updater.Services;

public sealed class InnoSetupApplier : IUpdateApplier
{
    private readonly string _installArgs;
    private bool _disposed;

    public InnoSetupApplier(string installArgs = "/VERYSILENT /SUPPRESSMSGBOXES /MERGETASKS=\"restart\"")
    {
        _installArgs = installArgs ?? "/VERYSILENT";
    }

    public void ApplyUpdate(string installerFilePath)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InnoSetupApplier));
        
        if (string.IsNullOrEmpty(installerFilePath))
            throw new ArgumentException("Installer file path cannot be null or empty", nameof(installerFilePath));

        if (!System.IO.File.Exists(installerFilePath))
            throw new System.IO.FileNotFoundException($"Installer not found: {installerFilePath}");

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = installerFilePath,
                Arguments = _installArgs,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = System.IO.Path.GetDirectoryName(installerFilePath)
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start installer process");
            }

            Debug.WriteLine($"[InnoSetupApplier] Installer started with PID: {process.Id}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InnoSetupApplier] Failed to launch installer: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}