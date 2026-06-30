# Spec: Auto-Update & IPC Stability Enhancements

## Objective
This specification addresses 5 architectural and release-hygiene issues to ensure robust real-time IPC control, safe sequential command processing, proper user prompts during update installation, clean diagnostic logging, and local process shutdown capabilities during update testing.

---

## Proposed Technical Changes

### 1. Bidirectional IPC Swap Acknowledgement
Currently, the UI sends a command over a one-way pipe and immediately assumes it succeeded. We will switch the named pipe direction to `PipeDirection.InOut` (bidirectional) on both the UI client and the AppRunner server:
* **AppRunner**: Processes the command and returns `"success"` or an `"error: <reason>"` string.
* **UI Client**: Awaits the string response from the pipe client with a `1.5s` read timeout. It only updates its active wallpaper index and visual states if the response is `"success"`.

### 2. IPC Command Serialization (AppRunner)
To prevent race conditions on shared media pipelines when rapid commands are received (e.g., rapid Live Swap clicks), we will serialize command execution in `AppRunner`'s IPC listener using a process-wide `SemaphoreSlim` (`_ipcLock`). Each command will be processed sequentially.

### 3. Settings "Install Update" Confirmation Routing
We will route the Settings page install action through the existing MainViewModel confirmation flow:
* **UI Settings**: Change the "Install update" button binding in `SettingsView.xaml` from `Updater.InstallCommand` to `RequestInstallUpdateCommand` in `MainViewModel`.
* This ensures that both the Minimal layout banner and the Settings page prompt the user before closing the app.

### 4. Remove Hard-coded Log Paths
Remove developer-specific synchronous file writes (`C:\Users\arnab\...`) from `UpdateCoordinator.cs` and `WindowsProcessManager.cs` to restore standard production logging locations.

### 5. Local Dotnet Process Detection Fallback
To prevent lock conflicts during local debugging (`dotnet run`), update `WindowsProcessManager.cs` to check for `dotnet.exe` / `dotnet` processes. If any are running and their command lines match `WallpaperTurbo.UI` or `WallpaperTurbo.AppRunner` (probed via `wmic.exe` process list queries), include them in the shutdown list.

---

## Commands
* Build: `dotnet build src\WallpaperTurbo.UI\WallpaperTurbo.UI.csproj`
* Test: `dotnet test tests\WallpaperTurbo.Tests\WallpaperTurbo.Tests.csproj`

---

## Code Style Example (Bidirectional IPC Client)
```csharp
private async Task<string> SendIpcCommandAsync(string command)
{
    try
    {
        using var client = new NamedPipeClientStream(".", "WallpaperTurbo_IPC", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(150);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync(command);

        using var reader = new StreamReader(client);
        using var cts = new CancellationTokenSource(1500);
        string? response = await reader.ReadLineAsync(cts.Token);
        return response ?? "error: timeout";
    }
    catch (Exception ex)
    {
        return $"error: {ex.Message}";
    }
}
```

---

## Testing Strategy
* **Unit Tests**:
  * Add a unit test to verify that the UI correctly updates its internal state only when the IPC swap command returns `"success"`.
  * Add a unit test verifying that `WindowsProcessManager` correctly detects `dotnet` debug host processes.
* **Manual Verification**:
  * Verify that clicking "Install update" in the Settings view opens the confirmation dialog.
  * Verify that rapid Live Swap clicks are processed sequentially without freezing the AppRunner media pipeline.

---

## Success Criteria
- [ ] Bidirectional IPC returns `"success"` or `"error: ..."` string.
- [ ] AppRunner processes commands sequentially via `_ipcLock` semaphore.
- [ ] Settings page install button triggers `RequestInstallUpdateCommand` (showing confirmation modal).
- [ ] No hardcoded developer paths exist in the codebase.
- [ ] `WindowsProcessManager` detects and shuts down debug-hosted `dotnet` processes.
- [ ] All unit tests compile and pass.
