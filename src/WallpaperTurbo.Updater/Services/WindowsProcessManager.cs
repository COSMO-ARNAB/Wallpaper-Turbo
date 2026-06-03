using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;

namespace WallpaperTurbo.Updater.Services;

public sealed class WindowsProcessManager : IProcessManager
{
    public async Task<bool> ShutdownAppRunnerGracefullyAsync(int timeoutMilliseconds)
    {
        var processes = Process.GetProcessesByName("WallpaperTurbo.AppRunner");
        var currentProcess = Process.GetCurrentProcess();
        var processesToShutdown = new List<Process>();

        foreach (var proc in processes)
        {
            try
            {
                if (proc.Id != currentProcess.Id && !proc.HasExited)
                {
                    processesToShutdown.Add(proc);
                }
            }
            catch
            {
            }
        }

        if (processesToShutdown.Count == 0)
        {
            return true;
        }

        var timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
        var tasks = new List<Task>();
        using var cts = new CancellationTokenSource(timeout);

        foreach (var proc in processesToShutdown)
        {
            tasks.Add(WaitForProcessExitAsync(proc, cts.Token));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[WindowsProcessManager] Timeout elapsed before all processes exited gracefully.");
            foreach (var proc in processesToShutdown)
            {
                ForceKillProcess(proc);
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsProcessManager] Error during graceful shutdown: {ex.Message}");
            foreach (var proc in processesToShutdown)
            {
                ForceKillProcess(proc);
            }
            return false;
        }

        foreach (var proc in processesToShutdown)
        {
            try
            {
                if (!proc.HasExited)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static async Task WaitForProcessExitAsync(Process process, CancellationToken token)
    {
        try
        {
            if (process.HasExited)
                return;

            process.CloseMainWindow();
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate to Task.WhenAll
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsProcessManager] Error requesting shutdown for PID {process.Id}: {ex.Message}");
        }
    }

    private static void ForceKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                Debug.WriteLine($"[WindowsProcessManager] Force killed process PID {process.Id}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsProcessManager] Failed to kill process PID {process.Id}: {ex.Message}");
        }
    }

    public void Dispose()
    {
    }
}