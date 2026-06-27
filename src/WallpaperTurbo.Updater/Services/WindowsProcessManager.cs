using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Interfaces;

namespace WallpaperTurbo.Updater.Services;

public sealed class WindowsProcessManager : IProcessManager
{
    #region agent log
    private static void DbgLog(string hypothesisId, string location, string message, object? data = null)
    {
        try
        {
            var line = System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = "e9f6e8",
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            System.IO.File.AppendAllText(@"C:\Users\arnab\PROJECTS\Wallpaper_Turbo\debug-e9f6e8.log", line + Environment.NewLine);
        }
        catch { }
    }
    #endregion

    public async Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds)
    {
        var processes = new List<Process>();
        processes.AddRange(Process.GetProcessesByName("WallpaperTurbo.AppRunner"));
        processes.AddRange(Process.GetProcessesByName("WallpaperTurbo.UI"));
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

        // #region agent log
        DbgLog("B", "WindowsProcessManager.cs:ShutdownEntry", "Processes targeted for shutdown", new
        {
            timeoutMilliseconds,
            currentPid = currentProcess.Id,
            targets = processesToShutdown.Select(p =>
            {
                try { return new { p.Id, p.ProcessName, hasExited = p.HasExited, mainWindow = p.MainWindowHandle != IntPtr.Zero }; }
                catch { return new { Id = -1, ProcessName = "?", hasExited = false, mainWindow = false }; }
            }).ToArray()
        });
        // #endregion

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

                // #region agent log
                DbgLog("C", "WindowsProcessManager.cs:AfterForceKill", "Process state after force-kill attempt", new
                {
                    proc.Id,
                    hadExitedBeforeKill,
                    hasExitedAfter = SafeHasExited(proc)
                });
                // #endregion

                if (!SafeHasExited(proc))
                {
                    allDead = false;
                }
            }
            shutdownResult = allDead;
        }
        finally
        {
            // #region agent log
            DbgLog("A", "WindowsProcessManager.cs:BeforeReturn", "Final process exit states before dispose", new
            {
                shutdownResult,
                processes = processesToShutdown.Select(p => new { p.Id, hasExited = SafeHasExited(p) }).ToArray()
            });
            // #endregion

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
            // #region agent log
            DbgLog("A", "WindowsProcessManager.cs:WaitForProcessExitAsync", "WaitForProcessExitAsync swallowed exception; task completes without exit guarantee", new
            {
                process.Id,
                process.ProcessName,
                exType = ex.GetType().Name,
                ex.Message,
                hasExited = SafeHasExited(process)
            });
            // #endregion
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

    public void Dispose()
    {
    }
}