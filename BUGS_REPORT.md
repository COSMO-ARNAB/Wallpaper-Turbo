# Wallpaper Turbo - AppRunner Bug & Architecture Audit Report

This report documents the security, concurrency, stability, and performance bugs identified within the `WallpaperTurbo.AppRunner` codebase (specifically in `Program.cs`, `NativeRenderWindow.cs`, and `WallpaperSessionManager.cs`).

---

## Executive Summary
Several critical and high-severity bugs were found in the `AppRunner` component. These include:
- Concurrent named pipe server issues that can lock up or crash communication with the UI.
- Delegate garbage collection bugs that trigger native access violations.
- Race conditions during multi-monitor class registration and window parenting.
- Micro-stuttering issues caused by over-aggressive and flawed memory trimming strategies.

---

## Detailed Findings

### 1. [Critical] CallbackOnCollectedDelegate MDA Violation (Access Violation / Crash)
- **Component**: `Program.cs` (around lines 52-58 and 509-510)
- **Symptom**: The application randomly crashes with a native access violation or `CallbackOnCollectedDelegate` Managed Debugging Assistant (MDA) exception, particularly during OS shutdown, logoff, or console event handling.
- **Root Cause**:
  ```csharp
  private static ConsoleCtrlDelegate? _consoleCtrlHandler;
  ...
  _consoleCtrlHandler = OnConsoleCtrl;
  SetConsoleCtrlHandler(_consoleCtrlHandler, true);
  ```
  While `_consoleCtrlHandler` is declared as a static field, in some runtimes/compilations, GC optimizations or class unloading can prematurely collect the delegate if the containing class is not kept alive or if it's re-assigned. More importantly, when detaching or on clean exit, `SetConsoleCtrlHandler(_consoleCtrlHandler, false)` is never invoked, leaving a dangling pointer in the kernel's console handler list.
- **Fix**:
  - Keep the delegate explicitly alive using `GC.KeepAlive(_consoleCtrlHandler)` at the end of the application's life.
  - Implement a proper `finally` block or detachment routine in `Main` that calls `SetConsoleCtrlHandler(_consoleCtrlHandler, false)`.

---

### 2. [Critical] NamedPipeServerStream Thread Safety & Access Violation
- **Component**: `Program.cs` (Named Pipe Command Server Loop)
- **Symptom**: Secondary instances or the UI app fails to send commands, or AppRunner locks up/crashes when multiple command connections are attempted.
- **Root Cause**:
  The named pipe connection loop reads incoming commands asynchronously using a single instance of `NamedPipeServerStream`. When a client disconnects, the code attempts to re-listen or reuse the stream without safe synchronization, or accesses stream properties concurrently from separate task continuations. This results in `InvalidOperationException` (Pipe is not yet connected) or `IOException` (Pipe is broken).
- **Fix**:
  - Re-create the `NamedPipeServerStream` inside a robust try-catch loop on every disconnect.
  - Use thread-safe locking or specialized channels (e.g., `System.Threading.Channels`) to pass commands from the named pipe thread to the rendering thread.

---

### 3. [High] Multi-Monitor Window Class Registration Race Condition (STA Threading)
- **Component**: `NativeRenderWindow.cs` (lines 45-119) and `WindowClassRegistrar.cs`
- **Symptom**: In multi-monitor systems, the wallpaper fails to render on secondary monitors, throwing Win32Exception: "Class already exists" or "Class does not exist".
- **Root Cause**:
  ```csharp
  Thread renderThread = new(() => {
      ...
      WindowClassRegistrar.Register(ClassName, WndProc);
      ...
  });
  ```
  Each monitor instantiates its own `NativeRenderWindow` which spawns an STA thread. Because these threads run concurrently, they both attempt to call `WindowClassRegistrar.Register` using the same static hardcoded `ClassName` ("WallpaperTurbo_RenderWindow_Class"). 
  - Thread A registers -> succeeds.
  - Thread B tries to register -> fails (Class already exists).
  - During shutdown, Thread A unregisters -> Thread B's window crashes or loses its window proc link because the class is unregistered while Thread B's window is still active.
- **Fix**:
  - Append a monitor identifier or unique GUID to the `ClassName` (e.g., `$"WallpaperTurbo_RenderWindow_Class_{monitor.Id}"`).
  - Implement thread-safe synchronization (using a lock) inside `WindowClassRegistrar` and keep a reference count of how many active windows are using the class before unregistering.

---

### 4. [High] Handle and Thread Leak on Explorer / Shell Restart
- **Component**: `Program.cs` & `ExplorerRestartMonitor.cs`
- **Symptom**: When Windows Explorer restarts (e.g., shell crash, taskbar restart), system resource usage spikes, and multiple duplicate processes/threads are created.
- **Root Cause**:
  When `ExplorerRestartMonitor` detects a shell restart, `AppRunner` attempts to re-create the wallpaper windows and re-parent them to the new `WorkerW` / `Progman` desktop window. However, the existing wallpaper window handles (`_hwnd`) and the associated STA message loop threads from `NativeRenderWindow` are not disposed of or shut down properly. The threads remain alive in background message loops, leaking GDI/user handles and memory.
- **Fix**:
  - Call `NativeRenderWindow.Shutdown(_hwnd)` for all active monitor sessions before re-parenting.
  - Join or await the termination of the old STA threads before initiating the new window creation.

---

### 5. [High] Uncaught Exceptions in Async Trimmer Loop
- **Component**: `Program.cs` (lines 1237-1250)
- **Symptom**: The memory trimmer loop silently dies after running for a few minutes/hours, leading to gradual memory leaks and performance degradation.
- **Root Cause**:
  ```csharp
  _ = Task.Run(async () => {
      while (!cts.Token.IsCancellationRequested) {
          try {
              await Task.Delay(10000, cts.Token);
              if (_sessionManager != null && _hwnd != IntPtr.Zero) {
                  TrimProcessMemory();
                  LogMemory("periodic.trimmed");
                  ...
              }
          } ...
      }
  });
  ```
  If `TrimProcessMemory` throws an exception (e.g., due to process handle access restrictions under specific security contexts or during disposal states), the exception might escape if `cts` is already cancelled, or other code pathways inside the task do not safely handle state transitions. In C# async tasks, an uncaught exception within a discarded `_ = Task.Run(...)` task is eaten by the runtime but silently terminates the loop.
- **Fix**:
  - Add broad `catch (Exception ex)` blocks to log and prevent thread termination.
  - Ensure the loop gracefully handles token cancellation without throwing `OperationCanceledException` to the thread pool.

---

### 6. [High] Wallpaper Parent/Child Window Hierarchy Positioning Race Condition
- **Component**: `Program.cs` (lines 1201-1225)
- **Symptom**: Wallpaper renders on top of regular desktop icons, obscures the taskbar, or appears offscreen.
- **Root Cause**:
  ```csharp
  IntPtr finalParent = NativeMethods.GetParent(_hwnd);
  if (finalParent != IntPtr.Zero) {
      NativeMethods.RECT prct = ...;
      NativeMethods.MapWindowPoints(IntPtr.Zero, finalParent, ref prct, 2);
      finalX = prct.Left;
      finalY = prct.Top;
  }
  ```
  If the parent of `_hwnd` is modified by the operating system (e.g., due to active desktop composition changes, virtual desktop switching, or WorkerW updates) right before or during `SetWindowPos`, `GetParent` may return an outdated or invalid handle. Mapping window points to an invalid handle yields garbled layout coordinates.
- **Fix**:
  - Ensure window parenting state is locked/synchronized during layout updates.
  - Verify that `finalParent` is still a valid window handle (`NativeMethods.IsWindow(finalParent)`) before performing coordinate mappings.

---

### 7. [Medium] Aggressive Memory Trimming Causing Stuttering (Working Set Pitfall)
- **Component**: `Program.cs` (lines 1232 and 1246)
- **Symptom**: Video or animated wallpapers suffer from micro-stuttering or periodic frame drops every 10 seconds.
- **Root Cause**:
  The application periodically calls `TrimProcessMemory()`, which invokes `SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1)` (equivalent to `EmptyWorkingSet`). While this artificially drops the "Working Set" memory shown in Task Manager (making the app look extremely lightweight), it forces the OS to page all active frames, texture buffers, and decode caches out to disk.
  Ten seconds later, the GPU/render loop has to page that memory back in from disk/pagefile, causing severe disk I/O bottlenecks and stuttering.
- **Fix**:
  - Remove the aggressive 10-second periodic `TrimProcessMemory()` during active rendering.
  - Limit Working Set trimming to startup, minimization, or when wallpaper playback is paused.

---

### 8. [Medium] Thread Safety in WallpaperSessionManager
- **Component**: `WallpaperSessionManager.cs`
- **Symptom**: Random NullReferenceExceptions or collections modified exceptions when monitors are plugged/unplugged or layout changes.
- **Root Cause**:
  Dictionary or list collections tracking active wallpaper sessions are mutated from different event threads (display configuration changes vs. UI named pipe commands) without locking mechanisms.
- **Fix**:
  - Replace raw lists/dictionaries with thread-safe collections like `ConcurrentDictionary<TKey, TValue>`.
  - Use `ReaderWriterLockSlim` or simple lock blocks during collection modification.

---

### 9. [Medium] Hardcoded Window Class Name Conflicts
- **Component**: `NativeRenderWindow.cs` (lines 42-43)
- **Symptom**: If the user runs multiple separate instances of Wallpaper Turbo (e.g., different users on a multi-user machine, or run-as-admin conflicts), the second instance fails to launch.
- **Root Cause**:
  The class name `WallpaperTurbo_RenderWindow_Class` is registered globally within the Windows session. A second instance attempting to register the identical class name will fail.
- **Fix**:
  - Inject a unique session ID or process ID into the class name registration.

---

### 10. [Low] Resource Leak in HardwareDetector
- **Component**: `HardwareDetector.cs` / `GpuPreference.cs`
- **Symptom**: Native handle leaks during GPU preference detection on startup.
- **Root Cause**:
  Calling DXGI/Direct3D native APIs to query GPU memory and capabilities leaves COM handles open. The objects are not properly released using `Marshal.ReleaseComObject` or through standard disposal patterns.
- **Fix**:
  - Implement a `using` statement or explicit `Marshal.ReleaseComObject()` on DXGI Factory, Adapter, and Device interfaces.
