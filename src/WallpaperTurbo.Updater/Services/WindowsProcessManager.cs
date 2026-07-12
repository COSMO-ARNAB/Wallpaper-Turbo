using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;

namespace WallpaperTurbo.Updater.Services;

public sealed class WindowsProcessManager : IProcessManager
{


    public async Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds)
    {
        var processes = new List<Process>();
        processes.AddRange(Process.GetProcessesByName("WallpaperTurbo.AppRunner"));
        processes.AddRange(Process.GetProcessesByName("WallpaperTurbo.UI"));

        // Fallback for local debugging: find running dotnet processes hosting our assemblies
        foreach (var proc in Process.GetProcessesByName("dotnet"))
        {
            try
            {
                string cmdLine = GetProcessCommandLine(proc.Id);
                if (cmdLine.Contains("WallpaperTurbo.UI", StringComparison.OrdinalIgnoreCase) ||
                    cmdLine.Contains("WallpaperTurbo.AppRunner", StringComparison.OrdinalIgnoreCase))
                {
                    processes.Add(proc);
                }
                else
                {
                    proc.Dispose();
                }
            }
            catch
            {
                proc.Dispose();
            }
        }

        using var currentProcess = Process.GetCurrentProcess();
        var processesToShutdown = new List<Process>();

        foreach (var proc in processes)
        {
            try
            {
                if (proc.Id != currentProcess.Id && !proc.HasExited)
                {
                    processesToShutdown.Add(proc);
                }
                else
                {
                    proc.Dispose();
                }
            }
            catch
            {
                proc.Dispose();
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

        bool shutdownResult = false;
        try
        {
            await Task.WhenAll(tasks);
            shutdownResult = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsProcessManager] Graceful shutdown completed with exceptions/timeout: {ex.Message}. Force killing remaining processes...");
            bool allDead = true;
            foreach (var proc in processesToShutdown)
            {
                bool hadExitedBeforeKill = false;
                try
                {
                    proc.Refresh();
                    hadExitedBeforeKill = proc.HasExited;
                    if (!proc.HasExited)
                    {
                        ForceKillProcess(proc);
                        proc.Refresh();
                    }
                }
                catch (Exception killEx)
                {
                    Debug.WriteLine($"[WindowsProcessManager] Error checking or killing process {proc.Id}: {killEx.Message}");
                }



                if (!SafeHasExited(proc))
                {
                    allDead = false;
                }
            }
            shutdownResult = allDead;
        }
        finally
        {


            foreach (var proc in processesToShutdown)
            {
                try { proc.Dispose(); } catch { }
            }
        }

        return shutdownResult;
    }

    private static bool SafeHasExited(Process proc)
    {
        try
        {
            proc.Refresh();
            return proc.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static async Task WaitForProcessExitAsync(Process process, CancellationToken token)
    {
        try
        {
            if (process.HasExited)
                return;

            try
            {
                process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsProcessManager] CloseMainWindow failed for PID {process.Id}: {ex.Message}");
            }

            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate to Task.WhenAll
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsProcessManager] Error waiting for PID {process.Id} exit: {ex.Message}");


            if (!SafeHasExited(process))
            {
                throw new InvalidOperationException($"Process {process.Id} did not exit and encountered error: {ex.Message}", ex);
            }
        }
    }

    private static void ForceKillProcess(Process process)
    {
        try
        {
            process.Refresh();
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000); // Wait to ensure OS releases locks
                process.Refresh();
                Debug.WriteLine($"[WindowsProcessManager] Force killed process PID {process.Id}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsProcessManager] Failed to kill process PID {process.Id}: {ex.Message}");
        }
    }

    public void ShutdownCurrentProcessGracefully()
    {
        using var currentProcess = Process.GetCurrentProcess();
        try
        {
            currentProcess.CloseMainWindow();
            
            // Fallback after 5 seconds if graceful shutdown fails
            Task.Delay(5000).ContinueWith(_ =>
            {
                Environment.Exit(0);
            });
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    private static string GetProcessCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");

            foreach (ManagementObject process in searcher.Get())
            {
                var commandLine = process["CommandLine"] as string;
                if (!string.IsNullOrWhiteSpace(commandLine))
                {
                    return commandLine;
                }
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
    }
}
