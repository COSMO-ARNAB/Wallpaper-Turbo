# Wallpaper Turbo — Renderer Architecture Specification

**Status:** Architecture finalization (pre-implementation)
**Author role:** Senior rendering-engine architect
**Audience:** Implementing engineers, code reviewers, future maintainers
**Predecessor docs:** `docs/libvlc-architecture-report.md`, `docs/libmpv-architecture-report.md`

---

## 0. How to read this spec

This document is the single source of truth for the renderer architecture. It is the **what** and the **why**. The implementation **how** belongs in code, with PRs that link back to specific sections.

The spec is ordered so that every later section depends only on earlier ones:

1. Scope, goals, non-goals, assumptions
2. Final renderer abstraction (`IRenderer` and friends)
3. Selection architecture (settings → factory → instance)
4. Capability model
5. Session and lifecycle ownership
6. Migration strategy (the 6-phase roadmap)
7. Risk review
8. Success criteria
9. Open questions

If any section contradicts the earlier ones, the earlier section wins. Open questions are blocking — they must be resolved before the corresponding phase begins implementation.

---

## 1. Scope, goals, non-goals, assumptions

### 1.1 Problem statement

Wallpaper Turbo currently renders wallpapers with a single backend: **LibVLC 3.0** via `LibVLCSharp`, embedded in a Win32 child window. The implementation lives in `WallpaperTurbo.Core` and is named `HardwareDecodePipeline : IMediaPipeline`. This pipeline works and is the production code path. However, the design embeds VLC concepts directly into the engine:

- `AspectRatio` / `CropGeometry` (VLC-specific)
- `MakeChildrenTransparent` (VLC spawns a child HWND that needs click-through styling)
- `Task.Delay(500)` after `Play()` to wait for VLC to spawn its child window
- A hardcoded path to `C:\Program Files\VideoLAN\VLC` for the native lib
- `LibVLCSharp` and `VideoLAN.LibVLC.Windows` referenced from `WallpaperTurbo.Core.csproj`

The engine wants the freedom to:

- Swap the renderer backend at runtime (Settings → "Renderer: VLC / MPV / Auto").
- Add more backends later (Windows Media Foundation, Web, Image, Scene) without rewriting the engine.
- Preserve all existing behavior. VLC must continue to work exactly as it does today for users who never change the setting.

### 1.2 Primary goal

> Create a stable, extensible renderer architecture that allows Wallpaper Turbo users to choose between VLC and MPV while preserving the current production-quality VLC experience.

### 1.3 Non-goals (explicit)

- **Not** to replace VLC. VLC remains the default and the safest path. The work is to make MPV *possible*, not to make VLC obsolete.
- **Not** to optimize for RAM in this architecture. RAM characteristics are an output of each renderer; the architecture only requires they are measurable.
- **Not** per-monitor renderer mixing in the initial release. The architecture must not prevent it, but the implementation will not support it.
- **Not** to introduce a DI container, IoC framework, or anything heavier than the current lightweight style.
- **Not** to rewrite working VLC code. The VLC implementation is production code. It must continue to be the default path with **zero behavior change** in phases 0–3.
- **Not** to remove `IMediaPipeline` immediately. It is a transitional adapter target and will be deprecated, then removed, only when nothing references it.
- **Not** to require an AMD/NVIDIA-specific benchmarking pass. Benchmarking is a phase 3 activity, gated on a stable abstraction.

### 1.4 Assumptions I am making

> These are the assumptions I had to fill in to write this spec. Correct any of them now and the spec will be revised before implementation.

1. **Single-process, many-sessions.** The wallpaper engine is one process; it may host multiple `WallpaperSession` instances (one per monitor in a future multi-monitor world). The renderer runs in-process. An out-of-process renderer design was explored in a disabled `MonitorSessionManager.cs` and is **not** in scope for this spec.
2. **Windows 10/11 only.** D3D11 is required. Win7/Win8 are not supported.
3. **Media is a local file path** in phase 0–3. The `WallpaperManifest.Video` field stays a single path. Network and stream sources are future work; the abstraction must not preclude them.
4. **Settings persist in JSON** at `%LocalAppData%\WallpaperTurbo\settings.json` (consistent with the existing manifest location pattern).
5. **One renderer instance per `WallpaperSession`.** A session owns one renderer. Future per-monitor mixing is then "different sessions may use different renderers" — no architectural change required.
6. **The renderer is constructed by an `IRendererFactory`** (interface), but the only implementation we ship is a local switch in code (no DI container, no reflection-loaded plugins). The interface exists so a future plugin loader can drop in.
7. **The current `Program.cs` stays the main entry point and stays monolithic.** Refactoring it is not part of this spec. We will add new types in new files; we will not move existing logic unless required.
8. **The renderer abstraction is C# / .NET 8 / Win32 only.** No cross-platform targets. No C++/CLI in the abstraction. Native interop is encapsulated inside each renderer implementation.
9. **The capability model is a data record** returned by each renderer. It is not a behavioral contract (no `WithTransparency()` extension methods). Consumers (e.g., future settings UI) read the record and act.
10. **No renderer runs more than one `WallpaperSession`.** Each session gets a fresh renderer instance. Renderer instances are not shared.
11. **`Auto` mode is a future flag, not a current behavior.** Phase 0–2 ship "VLC" and "MPV" only. Phase 5+ adds "Auto" with a documented fallback chain.
12. **Renderer choice is sticky per session.** Switching the renderer for a running session means stopping and recreating that session. The architecture does not promise hot-swap.
13. **License constraints matter.** VLC runtime is LGPL; MPV is GPL by default. We will ship the MPV LGPL build (`mpv-dev-lgpl`) and use dynamic linking, not static.

### 1.5 Decisions already made (reaffirmed)

These are the decisions the team has made. They are inputs to this spec, not outputs:

- **LGPL / libmpv is accepted** with documented packaging, deployment, and compliance requirements (§7.3 of this spec).
- **`IMediaPipeline` is deprecated, not deleted.** Preferred path: current pipeline → adapter → new abstraction → gradual migration.
- **Per-monitor renderer mixing is not for initial release** but the architecture must not preclude it.
- **Renderer fallback chain is a future "Auto" mode** and is part of the architecture.
- **No large DI framework.** Keep the current lightweight style; if DI is needed, keep it local.
- **Timeline: 5–6 weeks, correctness over speed.**
- **PR strategy: multiple small PRs**, each phase independently reviewable.

---

## 2. Final renderer abstraction

### 2.1 What goes inside the shared engine (WallpaperTurbo.Core)

The shared engine contains everything a renderer must do but is not tied to a specific backend. Concretely, the engine owns:

- **The renderer abstraction itself** (`IRenderer`, `IRendererCapabilities`, `IRendererDescriptor`).
- **Renderer registry and factory** (`IRendererFactory`, `RendererRegistry`).
- **A thin `IRenderer` adapter target** that wraps a new `IRenderer` to look like an `IMediaPipeline` for the existing `Program.cs` path. This is the only way `IMediaPipeline` survives phase 1.
- **The `MediaSource` and `RenderTarget` data records.**
- **`WallpaperSession` updated to hold an `IRenderer`** (the new owner of the playback surface).
- **Settings model** (`RendererSettings`, with `ActiveRenderer`, optional `FallbackRenderer`).
- **Renderer selection policy** (`RendererSelector`) that the engine uses to choose which `IRendererFactory` to call.

What the engine does **not** own:

- VLC-specific flags, options, paths, native handles.
- MPV-specific options, native handles, gl/d3d11 context management.
- Any reference to `LibVLCSharp`, `libmpv`, or any third-party native renderer library.
- Post-init rituals specific to a renderer (e.g., the `MakeChildrenTransparent` / `Task.Delay(500)` dance for VLC child windows).

> **Test for "is this in the right place?":** If the line of code mentions `LibVLC`, `libmpv`, `MPV_RENDER_PARAM_*`, `libvlc_*`, or any renderer's native handle, it is **not** in the engine.

### 2.2 What goes inside a renderer implementation (Player.Vlc, Player.Mpv, etc.)

Each renderer implementation is its own .NET project (assembly) and contains:

- **Its `IRenderer` implementation** (e.g., `VlcRenderer : IRenderer`).
- **Its `IRendererFactory` implementation** (e.g., `VlcRendererFactory : IRendererFactory`).
- **All references to its native library** (e.g., `LibVLCSharp` for VLC, `LibMpv.Client` for MPV).
- **All renderer-specific native interop** (P/Invoke declarations for `libmpv-2.dll`, D3D11 context queries, etc.).
- **All renderer-specific post-init rituals** that the engine shouldn't know about (the VLC `MakeChildrenTransparent` step, for example).
- **Its capability record** (e.g., `VlcCapabilities : IRendererCapabilities`).

The Player project is the only place that is allowed to import the renderer's native bindings. The engine depends on Player, not the other way around.

### 2.3 The `IRenderer` contract

```csharp
/// <summary>
/// A backend that can render a media source into a render target owned by the engine.
/// Implementations live in their own assembly (Player.Vlc, Player.Mpv, ...).
/// The engine depends only on this interface.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Stable identifier used by settings ("vlc", "mpv", ...). Lowercase, ASCII.</summary>
    string Id { get; }

    /// <summary>Human-readable display name ("VLC", "MPV", ...).</summary>
    string DisplayName { get; }

    /// <summary>Capabilities this renderer provides. Returned once, cheaply, after construction.</summary>
    IRendererCapabilities Capabilities { get; }

    /// <summary>
    /// True when the renderer is initialized and ready to Load. False otherwise.
    /// Implementations must be safe to call from any thread.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Allocate native resources (decoder, render context, audio config) and bind to the target.
    /// MUST NOT start playback. MUST be idempotent. MUST throw on failure with a stable exception.
    /// MUST be called from the same thread that will later call Load/Play, unless the implementation
    /// documents otherwise in its capability record.
    /// </summary>
    void Initialize(MediaSource source, RenderTarget target);

    /// <summary>
    /// Bind a media source to the already-initialized renderer. Safe to call before or after Play.
    /// MUST be idempotent. Switching sources mid-playback is allowed and is a stop+load+play sequence
    /// from the renderer's perspective (the engine does not need to call Pause first).
    /// </summary>
    void Load(MediaSource source);

    /// <summary>Begin or resume playback. Idempotent if already playing.</summary>
    void Play();

    /// <summary>Pause playback without releasing resources. Idempotent if already paused.</summary>
    void Pause();

    /// <summary>
    /// Stop playback and free as many resources as possible while staying ready to Resume.
    /// For a video renderer this typically means: stop the clock, drop decoded frames,
    /// keep the codec open. The engine calls Resume() to come back. Implementation-defined
    /// exactly how state is preserved.
    /// </summary>
    void Suspend();

    /// <summary>Counterpart of Suspend. Resumes from where Suspend left off, if the implementation supports it.</summary>
    void Resume();

    /// <summary>
    /// Apply a layout mode (fit/fill/stretch). The engine passes a single enum; the renderer maps it
    /// to its own options (VLC: aspect/crop, MPV: video-aspect/keepaspect/panscan).
    /// MUST be safe to call at any time after Initialize, including during Play.
    /// </summary>
    void ApplyLayout(WallpaperLayoutMode mode);

    /// <summary>
    /// Notify the renderer of a parent window size change. Default behavior is to call
    /// ApplyLayout(currentMode) again. Some renderers may override for efficiency.
    /// </summary>
    void NotifyTargetResized(int width, int height);

    /// <summary>Stop, release all native resources, mark IsInitialized = false. Idempotent.</summary>
    void Shutdown();
}
```

#### Why this contract and not a smaller one

The current `IMediaPipeline` is:

```csharp
public interface IMediaPipeline
{
    PipelineType Type { get; }
    void Initialize(IntPtr parentWindowHandle);
    void LoadMedia(string filePath);
    void Play();
    void Pause();
    void Suspend();
    void Resume();
    void SetTargetFps(int fps);
    void ApplyLayoutMode(WallpaperLayoutMode mode);
    void Release();
}
```

It looks similar. The differences that matter:

1. **`MediaSource` instead of `string filePath`.** A `MediaSource` is a record with `Kind` (File/Url/Stream) and `Location` (path or URI). Today's code only ever uses `File`, but the abstraction allows future expansion (network wallpapers, image sequences, scenes) without changing the contract.
2. **`RenderTarget` instead of `IntPtr parentWindowHandle`.** `RenderTarget` is a record that carries the HWND, the monitor info, and a `Bounds` rectangle. Today's code passes a single `IntPtr`; the abstraction is forward-compatible with future renderers that need more (a D3D11 device, a swap chain descriptor, etc.).
3. **`Id` and `DisplayName` on the renderer itself.** The factory uses these; the engine does not need a separate `RendererDescriptor` to ask for them.
4. **`IRendererCapabilities` is a first-class concept.** Renderers declare what they support; the engine never assumes.
5. **`Shutdown` is separate from `Dispose`.** `Shutdown` is the orderly stop. `Dispose` is the safety net (e.g., a finalizer path). This matches the VLC 3.0 destructor-thread pattern from the architecture report.
6. **No `PipelineType`.** The current enum (`SoftwareDecode`/`HardwareDecode`) leaks VLC's binary decision into the abstraction. Each `IRenderer` decides internally whether to use hardware decode (VLC: `--avcodec-hw=d3d11va`; MPV: `--hwdec=d3d11va`).
7. **No `SetTargetFps`.** The current method is a no-op stub (VLC manages timing internally). MPV has its own timing model. Neither renderer needs a public `SetTargetFps`; the engine should not pretend to control FPS.

### 2.4 Data records

```csharp
public sealed record MediaSource(MediaSourceKind Kind, string Location);

public enum MediaSourceKind { File, Url /*, Stream, ImageSequence, Scene — future */ }

public sealed record RenderTarget(
    IntPtr WindowHandle,
    MonitorInfo Monitor,
    PixelSize Size);

public sealed record PixelSize(int Width, int Height);
```

`MonitorInfo` already exists in `WallpaperTurbo.Core.Display` and is reused. `PixelSize` is new but small.

### 2.5 Initialization flow

```
1. AppRunner reads settings.
2. AppRunner constructs a MediaSource from the manifest entry.
3. AppRunner constructs a RenderTarget from the monitor + window.
4. AppRunner calls IRendererFactory.Create(rendererId) → IRenderer.
5. AppRunner calls IRenderer.Initialize(source, target).
   - VLC: creates LibVLC, MediaPlayer, sets Hwnd, applies flags.
   - MPV: sets LC_NUMERIC=C, calls mpv_create, sets options, mpv_initialize,
          creates render context, sets wid.
6. AppRunner calls IRenderer.Load(source) (or relies on Initialize to have loaded it — see §2.7).
7. AppRunner calls IRenderer.Play().
8. AppRunner calls IRenderer.ApplyLayout(currentMode) and IRenderer.NotifyTargetResized.
```

### 2.6 Shutdown flow

```
1. AppRunner calls IRenderer.Suspend()  [optional, e.g., on monitor disconnect]
2. AppRunner calls IRenderer.Shutdown()  [stops playback, releases native resources]
3. AppRunner calls IRenderer.Dispose()   [final cleanup, idempotent]
4. IRenderer instance is unreferenced; the engine does not pool renderers.
```

`Shutdown` is the orderly path. `Dispose` is the safety net. The VLC implementation in the architecture report calls `libvlc_media_player_stop_async` then `release` then waits for the destructor thread — that pattern fits cleanly into `Shutdown`. The current `HardwareDecodePipeline.Release` does roughly the right thing already; it just needs to be split into `Shutdown` + `Dispose`.

### 2.7 Initialization vs. Load

The current `IMediaPipeline.Initialize(IntPtr hwnd)` does not load media; it just sets up. `LoadMedia(path)` is called separately.

The new contract intentionally makes this explicit:

- `Initialize` sets up native resources, binds to the target, but does **not** start playback.
- `Load` binds a `MediaSource`. It can be called before or after `Play` (load-then-play is the normal flow; switching media mid-playback is "stop, load, play" from the renderer's point of view).

This is consistent with both VLC and MPV:

- VLC: `libvlc_media_player_new` → `libvlc_media_player_set_media` → `libvlc_media_player_play`.
- MPV: `mpv_create` → `mpv_initialize` → `mpv_set_option(wid)` → `mpv_command(loadfile)`.

### 2.8 Error handling

The contract is:

- `Initialize` throws on failure. Failure is unrecoverable: the renderer instance is unusable. The engine catches, logs, and may try the fallback (see §3.3).
- `Load` throws on a bad path / unreadable media. Same semantics.
- `Play`, `Pause`, `Resume`, `Suspend`, `ApplyLayout`, `NotifyTargetResized` are **best-effort**. They log and continue on failure; they do not throw.
- `Shutdown` is **never allowed to throw**. It catches and logs. This matches the defensive pattern in the current `HardwareDecodePipeline.Release` (try/catch around every stop/dispose call).

Rationale: by the time the engine is in steady state, throwing would tear down a session that may be in the middle of a stability recovery (Explorer restart, display change). The defensive pattern in the current code is correct; the new contract codifies it.

### 2.9 Threading model

- The engine **must not** call two `IRenderer` methods concurrently on the same instance. This is the engine's responsibility; the contract documents it but does not require renderers to be safe under concurrent calls.
- Renderers **may** marshal async work (e.g., the VLC `Task.Delay(500)` after `Play` to wait for child windows) internally. The engine does not see this.
- Renderers **must not** call back into the engine from internal threads. The engine listens to a small event surface on the renderer (see §2.10).
- The current `HardwareDecodePipeline` uses `lock(_sync)` to enforce single-threaded access. The new VLC implementation should keep this pattern. MPV's render API has stricter rules (§5.5 of the MPV architecture report) that will inform its locking.

### 2.10 Event surface (small and explicit)

The contract exposes a minimal event surface. Today, the only signal the engine needs is "I am initialized / playing / paused" — and even that is mostly inferred from the `IsInitialized` flag. A future revision may add a `IRendererEvents` interface for things like "end of file" (so the engine can loop, log, or switch renderers). For phase 0–2, this is not required.

```csharp
public interface IRenderer
{
    // ...lifecycle methods above...

    /// <summary>True when playback is in progress. False when paused, suspended, or stopped.</summary>
    bool IsPlaying { get; }
}
```

`IsPlaying` is the only state query the engine needs in phases 0–2. Everything else can be derived from method calls.

### 2.11 What is *not* in the contract (intentional omissions)

- **No `SetHwnd` / `SetTarget` re-binding.** The current code never rebinds the HWND; the renderer is created for a target. If a future multi-monitor architecture needs to move a renderer to a different window, it will create a new renderer.
- **No texture / surface access.** MPV's `mpv_render_context` and VLC's `libvlc_video_set_output_callbacks` both allow host-owned surfaces. We are not using those paths in the initial release. Adding them would couple the contract to graphics API concepts that the wallpaper engine does not need today.
- **No audio control.** Wallpaper is silent. `--no-audio` (VLC) / `ao=null` (MPV) is the default. If a future feature wants audio, it is a new contract.
- **No end-of-file / EOF event.** The current code uses `--loop` (VLC) and `loop-file=inf` (MPV). Looping is an engine-level concern, not a renderer contract.
- **No screenshot / OSD / volume / seek API.** The current engine does not need them. Adding them later is additive.

---

## 3. Renderer selection architecture

### 3.1 Flow

```
Settings (RendererSettings: ActiveRenderer, optional FallbackRenderer)
            │
            ▼
   RendererRegistry.Get(id) → RendererDescriptor
            │
            ▼
   IRendererFactory.Create(descriptor) → IRenderer
            │
            ▼
   WallpaperSession owns IRenderer
```

### 3.2 Settings model

```csharp
public sealed class RendererSettings
{
    /// <summary>"vlc" or "mpv". Defaults to "vlc".</summary>
    public string ActiveRenderer { get; set; } = "vlc";

    /// <summary>Optional. If non-null and non-empty, the engine falls back to this
    /// renderer when the active renderer fails to Initialize.</summary>
    public string? FallbackRenderer { get; set; }

    /// <summary>Per-renderer options (VLC: --vout, --avcodec-hw. MPV: --vo, --hwdec, --gpu-api).
    /// Keyed by renderer id. Default values are renderer-defined.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Options { get; set; }
        = new Dictionary<string, IReadOnlyDictionary<string, string>>();
}
```

The settings live in `%LocalAppData%\WallpaperTurbo\settings.json`. They are read once at AppRunner startup. The renderer choice is sticky for the session.

**Why a JSON file, not the registry, not user.config?**

- The current `WallpaperManifest.json` is already in `LocalAppData`. Consistency.
- The future `WallpaperTurbo.UI` (WPF) is the natural place to edit this file.
- JSON is easy to inspect by hand for debugging.

### 3.3 The `RendererRegistry` and `IRendererFactory`

```csharp
public interface IRendererFactory
{
    /// <summary>Stable id (e.g., "vlc", "mpv").</summary>
    string Id { get; }

    /// <summary>Display name ("VLC", "MPV").</summary>
    string DisplayName { get; }

    /// <summary>Build a fresh renderer instance. The engine calls this per session.</summary>
    IRenderer Create();

    /// <summary>
    /// Build a fresh renderer instance with extra options. Default: forward to Create().
    /// Renderers that need options (VLC needs the vout module, MPV needs the hwdec choice)
    /// override this to honor RendererSettings.Options.
    /// </summary>
    IRenderer Create(IReadOnlyDictionary<string, string> options);
}
```

The engine ships two factories:

- `VlcRendererFactory` in `WallpaperTurbo.Player.Vlc`
- `MpvRendererFactory` in `WallpaperTurbo.Player.Mpv`

`RendererRegistry` is a static, manually-populated list at AppRunner startup. No reflection, no MEF, no plugin discovery in the initial release. Adding a new renderer is a one-line registration in `Program.cs`.

```csharp
public static class RendererRegistry
{
    private static readonly Dictionary<string, IRendererFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(IRendererFactory factory) => _factories[factory.Id] = factory;

    public static IRendererFactory? Get(string id) =>
        _factories.TryGetValue(id, out var f) ? f : null;

    public static IEnumerable<IRendererFactory> All => _factories.Values;
}
```

### 3.4 `RendererSelector` and the fallback chain

```csharp
public static class RendererSelector
{
    /// <summary>
    /// Resolve the renderer to use, honoring fallback if the primary factory's Create
    /// or the resulting instance's Initialize throws. Returns null only if all candidates fail.
    /// </summary>
    public static IRenderer? Resolve(
        RendererSettings settings,
        MediaSource source,
        RenderTarget target);
}
```

`Resolve` tries `ActiveRenderer` first. If `Create` or `Initialize` throws, it tries `FallbackRenderer`. If both fail, it returns null and the engine logs an error and exits (the existing pattern for "no wallpapers found").

**This is the entire "fallback chain" feature.** There is no `IRenderer.Try` retry loop, no ranking, no `Auto` mode. `Auto` is a future flag that maps to "try MPV, fallback VLC" (or whatever the data shows is best) — the architecture is ready for it; the implementation is a one-liner in `Resolve` once we have the benchmark data from phase 3.

### 3.5 What "no code changes required" means in this spec

The user can change the renderer via Settings — true. They do not need to recompile Wallpaper Turbo — true. The selection is data-driven.

The user does need to be on a version of Wallpaper Turbo that has the MPV renderer installed. That is a deployment decision (phase 4, §6). The architecture supports bundling both renderers in the installer.

### 3.6 The `IMediaPipeline` adapter (the only bridge)

`IMediaPipeline` survives phase 1 as a transitional adapter target. The new `VlcRenderer` is wrapped in a small class that:

```csharp
internal sealed class VlcPipelineAdapter : IMediaPipeline
{
    private readonly IRenderer _renderer;
    // Initialize / LoadMedia / Play / Pause / Suspend / Resume / ApplyLayoutMode / Release
    // all delegate to the wrapped IRenderer.
    public PipelineType Type => PipelineType.HardwareDecode;  // preserves the existing enum
}
```

`Program.cs` does not need to change in phase 1. It continues to construct `HardwareDecodePipeline`. The constructor changes to:

```csharp
public HardwareDecodePipeline(bool useSoftwareDecode, string? videoOutputModule)
{
    var settings = new RendererSettings
    {
        ActiveRenderer = "vlc",
        Options = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["vlc"] = new Dictionary<string, string>
            {
                ["useSoftwareDecode"] = useSoftwareDecode ? "true" : "false",
                ["vout"] = videoOutputModule ?? "",
            }
        }
    };
    var factory = RendererRegistry.Get("vlc")!;
    _renderer = factory.Create(settings.Options["vlc"]);
    // ...
}
```

This is one of the rare legitimate reasons to touch `HardwareDecodePipeline.cs` in phase 1. The renderer is the same; only the construction path changes. **The behavior of the renderer is unchanged.**

Phase 2 introduces an MPV adapter path. The engine can use `VlcPipelineAdapter` or a future `MpvPipelineAdapter` interchangeably. The selection is data-driven.

`IMediaPipeline` is marked `[Obsolete("Will be removed in a future release; use IRenderer directly.")]` in phase 1. It is deleted in a later phase (estimate: phase 5, after the MPV path has shipped and the adapter is no longer needed). Deletion is gated on **zero references** in the engine.

---

## 4. Renderer capability model

### 4.1 Why capabilities exist

Each renderer has different characteristics. A future settings UI may want to grey out options that a renderer does not support. A diagnostic logger may want to know what was negotiated. The capability record is a **data-only** answer to these questions.

The capability model is **not** a behavioral contract. There is no `IRenderer.WithTransparency()` and no `IRendererExtensions` that grow the interface. The engine reads the record and acts.

### 4.2 The `IRendererCapabilities` record

```csharp
public interface IRendererCapabilities
{
    /// <summary>Renderer can play local video files.</summary>
    bool VideoSupported { get; }

    /// <summary>Renderer can decode video on the GPU (D3D11VA / NVDEC / Vulkan Video).</summary>
    bool HardwareDecodeSupported { get; }

    /// <summary>Renderer can drive HDR (HDR10, HLG) video output.</summary>
    bool HdrSupported { get; }

    /// <summary>Renderer can render to a host-owned HWND (the wallpaper window).
    /// All renderers in the initial release must be true here.</summary>
    bool HwndEmbeddingSupported { get; }

    /// <summary>Renderer can pause and resume without fully stopping the engine.
    /// True means Suspend/Resume round-trips are cheap and state-preserving.</summary>
    bool CheapSuspendSupported { get; }

    /// <summary>Renderer can render the same content to multiple HWNDs (multi-monitor).
    /// Phase 0–2: false. Future: true for some renderers.</summary>
    bool MultiMonitorSupported { get; }

    /// <summary>List of HW decode backends the renderer prefers / supports.
    /// E.g., VLC: ["d3d11va", "dxva2"]. MPV: ["d3d11va", "nvdec", "vulkan"].</summary>
    IReadOnlyList<string> HardwareDecodeBackends { get; }

    /// <summary>Renderer-specific notes for diagnostics.</summary>
    IReadOnlyDictionary<string, string> Metadata { get; }
}
```

### 4.3 Initial capability records

```csharp
// VLC 3.0.x via LibVLCSharp 3.9.x
public sealed class VlcCapabilities : IRendererCapabilities
{
    public bool VideoSupported => true;
    public bool HardwareDecodeSupported => true;          // --avcodec-hw=d3d11va works
    public bool HdrSupported => true;                      // d3d11 vout supports HDR10/HLG
    public bool HwndEmbeddingSupported => true;
    public bool CheapSuspendSupported => true;             // Stop+replay is fast on local files
    public bool MultiMonitorSupported => true;             // one LibVLC instance per session, scales fine
    public IReadOnlyList<string> HardwareDecodeBackends => new[] { "d3d11va", "dxva2" };
    public IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>
    {
        ["nativeLib"] = "libvlc.dll",
        ["nativeLibVersion"] = "3.0.21",  // matches VideoLAN.LibVLC.Windows package version
        ["license"] = "LGPL-2.1-or-later"
    };
}

// libmpv (LGPL build) 2.x
public sealed class MpvCapabilities : IRendererCapabilities
{
    public bool VideoSupported => true;
    public bool HardwareDecodeSupported => true;          // --hwdec=d3d11va works
    public bool HdrSupported => true;                      // gpu-next + libplacebo supports HDR10/HLG
    public bool HwndEmbeddingSupported => true;           // via --wid=
    public bool CheapSuspendSupported => true;             // pause is cheap; full suspend is stop+reload, slower
    public bool MultiMonitorSupported => true;             // one mpv instance per session
    public IReadOnlyList<string> HardwareDecodeBackends => new[] { "d3d11va", "nvdec", "vulkan" };
    public IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>
    {
        ["nativeLib"] = "libmpv-2.dll",
        ["nativeLibVersion"] = "0.x.x",  // pinned in phase 2
        ["license"] = "LGPL-2.1-or-later"  // only when using mpv-dev-lgpl build
    };
}
```

### 4.4 What the engine does with capabilities

In phases 0–2, the engine only uses `VideoSupported` (to refuse to start a non-video renderer against a video source). Everything else is informational, logged at startup for diagnostics:

```
[Renderer] Active: vlc
[Renderer]   Native: libvlc.dll 3.0.21 (LGPL-2.1-or-later)
[Renderer]   Hardware decode: yes (backends: d3d11va, dxva2)
[Renderer]   HDR: yes
[Renderer]   Multi-monitor: yes
```

Future consumers:

- **Settings UI (phase 4):** grey out HDR checkbox for renderers that don't support it.
- **Auto mode (phase 5):** rank renderers by hardware decode availability, prefer the one that has `d3d11va` in `HardwareDecodeBackends`.
- **Benchmarking (phase 3):** record per-renderer capabilities alongside the perf numbers.

### 4.5 What the engine does **not** do with capabilities

- It does not branch on `HdrSupported` to enable/disable HDR rendering. The user picks HDR in the manifest; the renderer either handles it or logs a warning. The capability record is not a gate.
- It does not refuse to load a video because `VideoSupported` is false. The record is documentation; the engine is the one that decides what content it serves. If the engine ever needs to serve non-video sources, that is a future, larger change.

---

## 5. Session and lifecycle architecture

### 5.1 Ownership model

```
WallpaperSession
├── owns: WindowHandle (HWND)            [lifetime = session]
├── owns: MediaSource (record)           [lifetime = session]
├── owns: IRenderer (instance)           [lifetime = session]
│     ├── owns: native decoder/render    [lifetime = renderer]
│     ├── owns: window binding           [lifetime = renderer]
│     └── owns: any GPU resources        [lifetime = renderer]
├── references: MonitorInfo              [lifetime = session, mutable]
└── references: WallpaperEntry           [lifetime = session]
```

The session is the single owner. The renderer does not outlive the session. The session does not outlive the window.

### 5.2 Lifecycle state machine

```
       ┌──────────┐
   ┌──►│   New    │
   │   └────┬─────┘
   │        │ Initialize
   │        ▼
   │   ┌──────────┐
   │   │   Init   │  (renderer.IsInitialized == true, no media)
   │   └────┬─────┘
   │        │ Load
   │        ▼
   │   ┌──────────┐
   │   │  Loaded  │  (media bound, not playing)
   │   └────┬─────┘
   │        │ Play
   │        ▼
   │   ┌──────────┐         Suspend         ┌──────────┐
   │   │ Playing  │◄────────────────────────┤Suspended │
   │   └────┬─────┘         Resume          └────▲─────┘
   │        │                                     │
   │        │ Pause                                │
   │        ▼                                     │
   │   ┌──────────┐                               │
   │   │  Paused  │───────────────────────────────┘
   │   └────┬─────┘                  Pause/Resume
   │        │ Load (new media)
   │        ▼
   │   ┌──────────┐
   │   │  Loaded  │  (back to Loaded with new source)
   │   └────┬─────┘
   │        ...
   │        │ Shutdown
   │        ▼
   │   ┌──────────┐
   │   │ Stopped  │
   │   └────┬─────┘
   │        │ Dispose
   │        ▼
   │   ┌──────────┐
   │   │Disposed  │
   │   └──────────┘
```

- `Shutdown` is reachable from any state. After `Shutdown`, the renderer is unusable; the engine creates a new one.
- `Dispose` is the last call. After `Dispose`, the session is dead.
- `Pause` from `Suspended` is a no-op (stays in `Suspended`).
- `Suspend` from `Paused` is valid; goes to `Suspended`.
- `Resume` from `Paused` returns to `Playing`. (Implementation note: this is a no-op for some renderers, e.g., MPV's `set_property pause no`; for VLC, `Play()` is idempotent.)

### 5.3 The updated `WallpaperSession`

```csharp
public sealed class WallpaperSession : IDisposable
{
    public IntPtr WindowHandle { get; }
    public MonitorInfo Monitor { get; private set; }
    public WallpaperEntry Wallpaper { get; }
    public IRenderer Renderer { get; }

    public WallpaperSession(IntPtr hwnd, WallpaperEntry wallpaper, IRenderer renderer, MonitorInfo monitor)
    { ... }

    public void UpdateMonitor(MonitorInfo m) { ... }

    public void Play() => Renderer.Play();
    public void Pause() => Renderer.Pause();
    public void Suspend() => Renderer.Suspend();
    public void Resume() => Renderer.Resume();

    public void Dispose()
    {
        try { Renderer.Shutdown(); } catch { }
        Renderer.Dispose();
    }
}
```

The shape mirrors the current `WallpaperSession` almost exactly. The only meaningful change is `IMediaPipeline MediaPipeline` → `IRenderer Renderer`. The contract methods are the same names; the engine code in `Program.cs` that calls them does not change in phase 1.

### 5.4 Resource cleanup

Cleanup order is the single most fragile part of the architecture. The current code in `Program.cs` has subtle patterns that the new model must preserve:

- **`Shutdown` first, then `Dispose`.** The VLC destructor-thread pattern from the architecture report (§4.1) requires `libvlc_media_player_release` to be called from a thread that is willing to wait. The current code calls `Release` synchronously on the main thread. The new contract codifies this: `Shutdown` does the orderly work, `Dispose` is the safety net.
- **Defensive `try/catch` around every native call.** Today's `HardwareDecodePipeline.Release` catches and continues. The new contract requires this for `Shutdown` and `Dispose`. The implementation must not let a thrown exception from one renderer's cleanup method abort the cleanup of the next.
- **No `finalize`/`destructor` on the session.** Sessions are not finalizer targets; if `Dispose` is missed, the OS will reclaim the process, and the process death will reclaim native handles. This is the existing behavior and is acceptable.
- **Async shutdown is allowed but not required.** The current code has a background `_ = Task.Run(...)` for cleanup during Explorer restart recovery. The new contract allows this (it is the caller's choice). The contract itself is synchronous to keep the engine simple; an async variant (`ShutdownAsync`) is a future addition.

### 5.5 Crash recovery

The current `Program.cs` has a sophisticated Explorer-restart recovery loop: it waits for Progman/WorkerW to reappear, then recreates the render window, then constructs a new `HardwareDecodePipeline`. This logic stays in `Program.cs`. It does not move into the renderer abstraction.

The only change: instead of `new HardwareDecodePipeline(...)`, the recovery code uses `RendererSelector.Resolve(settings, source, target)`. If the user picked VLC and the recovery works, the recovery picks VLC. If they picked MPV and the recovery works, the recovery picks MPV. The recovery is renderer-agnostic.

This is the test: **does the Explorer-restart recovery work identically for VLC and MPV?** If yes, the architecture is sound. If no, the abstraction is leaking.

### 5.6 Wallpaper switching

Wallpaper switching is `Shutdown` + recreate. The current code does not switch wallpapers in place (the manifest index is fixed for a process). The architecture supports it but does not require it.

### 5.7 Future multi-monitor

The architecture supports multi-monitor by having multiple `WallpaperSession` instances. Each owns its own renderer. A future "different renderer per monitor" feature is a different factory call per session, not a different abstraction. The `MonitorInfo` is on the `RenderTarget`, which is on the session. The renderer is constructed with that target. Nothing in this spec prevents a future code path that maps monitor → factory dynamically.

---

## 6. Migration strategy (6-phase roadmap)

Each phase is **independently releasable**. The VLC path remains the default and the only path in phases 0–2. Nothing about the existing user experience changes in phases 0–2.

### 6.1 Phase 0 — Renderer abstraction (no behavior change)

**Goal:** Introduce `IRenderer` and friends. Ship with the VLC implementation as the only renderer. Zero behavior change for users.

**Changes:**

1. Add `MediaSource`, `RenderTarget`, `PixelSize` records to `WallpaperTurbo.Core.Media`.
2. Add `IRenderer`, `IRendererCapabilities`, `IRendererFactory` to `WallpaperTurbo.Core.Media`.
3. Add `RendererRegistry`, `RendererSettings`, `RendererSelector` to `WallpaperTurbo.Core.Media`.
4. Add `RendererSettings` JSON load/save to `WallpaperTurbo.Core.Services` (or new `WallpaperTurbo.Core.Configuration`).
5. Refactor `WallpaperTurbo.Core.csproj` to **remove** the `LibVLCSharp` and `VideoLAN.LibVLC.Windows` package references. Move them to a new `WallpaperTurbo.Player.Vlc` project.
6. Create `WallpaperTurbo.Player.Vlc.csproj` referencing the new packages.
7. Move `HardwareDecodePipeline.cs` to `WallpaperTurbo.Player.Vlc`. Rename to `VlcRenderer.cs`. Have it implement `IRenderer` and `IMediaPipeline` (the adapter pattern from §3.6).
8. Add `VlcRendererFactory.cs` to `WallpaperTurbo.Player.Vlc`. Register the factory in `Program.cs`.
9. Add a `VlcPipelineAdapter : IMediaPipeline` that wraps `VlcRenderer`. `HardwareDecodePipeline` becomes a thin shell around the adapter (preserves the existing public type for any external code that references it).
10. Verify: existing build succeeds, all existing tests pass (if any), manual smoke test confirms identical behavior.

**Acceptance criteria:**

- [ ] No file in `WallpaperTurbo.Core` references `LibVLCSharp` or `libvlc` symbols.
- [ ] `Program.cs` line count does not decrease by more than 5% (no big-bang refactor).
- [ ] VLC playback of `red-leaves.mp4` and `crimson-blind.mp4` (the bundled test wallpapers) is byte-identical to the previous build's behavior.
- [ ] Explorer-restart recovery works.
- [ ] Display-change recovery works.
- [ ] Pause-mode (Focused / Maximized) works.
- [ ] Memory trim loop still runs at 10s interval.
- [ ] `--software-decode` and `--vout` CLI args still work.

**PRs (small, independently reviewable):**

- PR 0a: Add `IRenderer` and friends. No engine wiring. (Build green; nothing uses it yet.)
- PR 0b: Add `VlcRenderer` and `VlcRendererFactory` in `Player.Vlc`. Register the factory.
- PR 0c: Move VLC packages out of Core. Adjust `.csproj` files. Verify build.
- PR 0d: Wire `Program.cs` to use the new factory path while preserving CLI args.

Each PR is reviewable on its own. Each PR is releasable on its own (with the engine still using the adapter path). At the end of phase 0, the architecture is in place but VLC is the only renderer.

### 6.2 Phase 1 — VLC adapter stabilization (no behavior change)

**Goal:** Stabilize the adapter. Confirm the contract works for the real VLC renderer. Catch any contract gaps.

**Changes:**

1. Run the full smoke test matrix (bundled wallpapers, both pause modes, both decode modes).
2. Add unit tests for `VlcRenderer` lifecycle: Initialize, Load, Play, Pause, Suspend, Resume, Shutdown, Dispose.
3. Add unit tests for `RendererSelector` with no fallback, with fallback, and with both failing.
4. Mark `IMediaPipeline` `[Obsolete]`.
5. Document the contract (XML doc comments) — every method on `IRenderer` has a clear, reviewed specification.

**Acceptance criteria:**

- [ ] All `IRenderer` methods have a clear XML doc comment that survived a reviewer.
- [ ] All `VlcRenderer` lifecycle tests pass.
- [ ] The smoke test matrix passes for VLC.
- [ ] `IMediaPipeline` is `[Obsolete]` but still functional.
- [ ] No regression in any of the 12 stability / performance behaviors from phase 0.

**No new PRs in this phase** — this is a stabilization phase, not a feature phase.

### 6.3 Phase 2 — MPV prototype (off by default)

**Goal:** Add an MPV renderer. It is shipped in the binary, registered in the factory, but the default `RendererSettings.ActiveRenderer` is still `"vlc"`. The MPV path is opt-in.

**Changes:**

1. Create `WallpaperTurbo.Player.Mpv.csproj` referencing the chosen MPV bindings package (decision in §7.3).
2. Add `MpvRenderer : IRenderer` and `MpvRendererFactory : IRendererFactory`.
3. Ship `libmpv-2.dll` (LGPL build) alongside `libvlc.dll` in the install directory.
4. Document the MPV-specific options in `RendererSettings.Options["mpv"]`.
5. Add the MPV path to the smoke test matrix.
6. The CLI argument `--renderer mpv` is added for testing without going through Settings.

**Acceptance criteria:**

- [ ] `libmpv-2.dll` is present in the install directory next to `libvlc.dll`.
- [ ] The MPV renderer is registered but not the default.
- [ ] `WallpaperTurbo.exe --renderer mpv` plays `red-leaves.mp4` with MPV.
- [ ] All IRenderer lifecycle methods work for MPV.
- [ ] Pause-mode, Explorer restart, display change, memory trim all work for MPV.
- [ ] VLC playback is unchanged.

**PRs:**

- PR 2a: Add `MpvRenderer` skeleton (constructor only, no Initialize). Build green.
- PR 2b: Implement Initialize, Load, Play, Pause, Shutdown. Manual smoke test.
- PR 2c: Implement Suspend, Resume, ApplyLayout, NotifyTargetResized.
- PR 2d: Add MPV to the smoke test matrix; add unit tests.
- PR 2e: Add `--renderer` CLI argument.

### 6.4 Phase 3 — Benchmarking (no UI)

**Goal:** Measure both renderers on representative hardware. Capture data for the future Auto mode and the Settings UI.

**Changes:**

1. Build a benchmark harness in `tools/benchmark/` that:
   - Loads both wallpapers (1080p, 4K if available).
   - Plays each on VLC and MPV.
   - Records: working set (private bytes), GPU memory (DXGI query), CPU%, frame drops, first-frame latency.
   - Runs for a configurable duration.
2. Run on at least 3 hardware configurations: Intel iGPU, NVIDIA dGPU, AMD dGPU.
3. Record results to `docs/benchmark-results-2026-Q2.md`.
4. No code change to the engine.

**Acceptance criteria:**

- [ ] Benchmark harness runs to completion on all 3 configs.
- [ ] Results are reproducible (variance documented).
- [ ] No benchmark data is needed to make the spec work — it is for future decisions only.

**No engine code PRs in this phase.** A single repo addition: `tools/benchmark/`.

### 6.5 Phase 4 — Experimental release

**Goal:** Ship Wallpaper Turbo with both renderers available. The Settings file is the only way to switch. CLI arg is hidden. This is a public release; users may try MPV.

**Changes:**

1. Default `RendererSettings.ActiveRenderer` is still `"vlc"`.
2. A future WPF settings UI may or may not exist; for this phase, the user edits `settings.json` by hand or through a simple readme instruction.
3. Documentation (`docs/user-renderer-selection.md`) explains the option, the trade-offs, and the rollback (delete the file, VLC is the default).
4. Collect user feedback for at least 2 release cycles.

**Acceptance criteria:**

- [ ] Both renderers ship in the installer.
- [ ] `settings.json` change alone switches the renderer.
- [ ] No crash on either renderer with bundled wallpapers.
- [ ] Rollback (delete `settings.json`) restores VLC.
- [ ] Telemetry (if any) shows no increase in error rate vs. phase 0.

**PRs:**

- PR 4a: Bundle both renderers in the installer. Adjust Inno Setup script.
- PR 4b: Documentation.

### 6.6 Phase 5 — Settings UI + deprecate `IMediaPipeline`

**Goal:** Add a user-facing settings UI for renderer selection. Delete `IMediaPipeline` and the adapter.

**Changes:**

1. Add a Settings page to `WallpaperTurbo.UI` (WPF) that:
   - Lists registered renderers from `RendererRegistry.All`.
   - Shows capabilities for each.
   - Lets the user pick the active renderer.
   - Lets the user pick the fallback renderer (with "none" option).
2. Persists to `settings.json` via the existing `RendererSettings` model.
3. Delete `VlcPipelineAdapter` and `IMediaPipeline` once no references remain. Run a grep.
4. Update `Program.cs` to construct `IRenderer` directly (not via the adapter).

**Acceptance criteria:**

- [ ] Settings UI works; user can pick a renderer and the change takes effect on next launch.
- [ ] No `IMediaPipeline` references in the codebase.
- [ ] The build does not contain `WallpaperTurbo.Core.Media.IMediaPipeline` symbol.

**PRs:**

- PR 5a: Settings UI page (UI only; writes to JSON).
- PR 5b: Delete adapter and `IMediaPipeline`. Update engine.

### 6.7 Phase 6 — Auto / fallback mode

**Goal:** The `Auto` option. Try MPV; fall back to VLC on Initialize failure.

**Changes:**

1. Add a new value `"auto"` to `RendererSettings.ActiveRenderer`.
2. `RendererSelector` interprets `"auto"` as a fixed-order chain (the chain is configurable; default is MPV first, VLC second). The chain may be informed by phase 3 benchmark data.
3. The chain is documented in the settings UI.
4. Optional: allow the user to provide their own ordered chain.

**Acceptance criteria:**

- [ ] "Auto" mode picks MPV on a machine that has it.
- [ ] "Auto" mode falls back to VLC when MPV's `Initialize` throws.
- [ ] The fallback is logged at startup.

**PRs:**

- PR 6a: Add "auto" semantics to `RendererSelector`.
- PR 6b: Update settings UI to expose the chain.

### 6.8 Phase 0–6 summary

| Phase | VLC default? | MPV available? | Settings UI? | Auto mode? | Risk to VLC? |
|---|---|---|---|---|---|
| 0 | Yes | No | No | No | Zero |
| 1 | Yes | No | No | No | Zero |
| 2 | Yes | Yes (opt-in) | No | No | Zero |
| 3 | Yes | Yes | No | No | Zero |
| 4 | Yes | Yes | No | No | Low |
| 5 | Yes | Yes | Yes | No | Low |
| 6 | Yes | Yes | Yes | Yes | Low |

The architecture is live from phase 0 onward. The risk profile is non-zero only from phase 4 (when the MPV path is user-facing).

---

## 7. Risk review

This section challenges the architecture's assumptions. Each risk is rated as **Low / Medium / High** and has a mitigation.

### 7.1 Technical risks

#### R-T1 (High): VLC hardcoded path is fragile.

**The risk:** `HardwareDecodePipeline.cs:60-67` throws `DirectoryNotFoundException` if `C:\Program Files\VideoLAN\VLC` is missing. The new `VlcRenderer` must not propagate this fragility. If the user uninstalls system VLC, the wallpaper engine must keep working.

**Mitigation:** The `VlcRenderer` resolves the native library path as follows, in order:

1. `RendererSettings.Options["vlc"]["nativeLibPath"]` (explicit override, set by the installer or by the user).
2. `%LocalAppData%\WallpaperTurbo\runtimes\win-x64\native\libvlc.dll` (bundled, the default).
3. `C:\Program Files\VideoLAN\VLC\libvlc.dll` (legacy, system-installed VLC).

The bundled path is the default in the installer. The system-VLC path is the fallback for users who have a custom VLC install. This change is required in phase 0.

#### R-T2 (High): VLC `MakeChildrenTransparent` and `Task.Delay(500)` are undocumented rituals.

**The risk:** These work today, but they are not in any contract. A future engineer (or a future VLC update) could break them. The architecture must not let them leak into `IRenderer`.

**Mitigation:** They stay in `VlcRenderer.Play()` (or a private helper called from `Play`). They are documented in the `VlcRenderer` class header. They are tested: a unit test asserts that after `Play()`, the parent HWND's child windows are styled for click-through within a 1s window.

#### R-T3 (Medium): MPV's render API threading rules are stricter than VLC's.

**The risk:** Per the MPV architecture report (§5.5), `MPV_RENDER_PARAM_ADVANCED_CONTROL=1` requires strict threading: the render thread owns the device, no other API calls happen there, etc. The current `HardwareDecodePipeline` does not use `mpv_render_context` at all (it uses `--wid` only). If we keep `--wid`-only, the threading rules are much simpler. If we add a render context for composition, we need a real render thread.

**Mitigation:** For phase 2, the MPV renderer uses `--wid` only (no `mpv_render_context`). The "wallpaper embedding via `wid`" pattern is the lowest-risk path. A future phase may add a render context for HDR passthrough or scene composition. This is a phase 7+ concern, not in this spec.

#### R-T4 (Medium): MPV has no "Install" step on Windows.

**The risk:** libmpv is distributed as a single DLL. There is no system-wide install. We have to ship the DLL ourselves.

**Mitigation:** The installer includes `libmpv-2.dll` (LGPL build) at the same location where `libvlc.dll` lives. The directory is added to the Win32 DLL search path implicitly because the engine's `.exe` is in the same directory. The `VlcRenderer`/`MpvRenderer` do not need to do anything special.

#### R-T5 (Medium): MPV may need a VC++ Redistributable that VLC does not.

**The risk:** Per the MPV architecture report (§10.3), `mpv-dev-lgpl` is built with MSVC and needs `vcruntime140.dll`, `msvcp140.dll`, `vcruntime140_1.dll`. Some Windows installs do not have these.

**Mitigation:** The installer ships the VC++ runtime side-by-side with the engine. Alternative: detect the missing DLLs and log a helpful error. This is a deployment decision in phase 2 / 4.

#### R-T6 (Medium): Renderer-native handles can outlive their declared lifetime.

**The risk:** VLC's `libvlc_media_player_release` is synchronous in 3.0 ≤ 3.0.11, semi-async in 3.0.12+. The current code calls `_mediaPlayer.Dispose()` and assumes the call is fast. The architecture report (R-T1's foundation) calls this out as a "trap door."

**Mitigation:** The contract says `Shutdown` is synchronous and may take "a frame's worth of time" (i.e., up to ~100ms for VLC's destructor thread). The engine calls `Shutdown` on a non-UI thread (the existing `Task.Run` pattern in `Program.cs` already does this for cleanup during stability recovery). Document the timing expectation in `IRenderer.Shutdown`'s XML doc.

#### R-T7 (Low): Capability records may diverge from reality.

**The risk:** A renderer reports `HdrSupported = true` but is in a configuration that disables HDR (e.g., a non-HDR monitor). The record is wrong.

**Mitigation:** Capabilities describe the **renderer's potential**, not the **current configuration**. The engine and the user are responsible for matching capability to environment. A future revision may add a `Probe()` method that returns runtime capability; that is a phase 7+ concern.

### 7.2 Architectural risks

#### R-A1 (High): `IMediaPipeline` adapter becomes a permanent tax.

**The risk:** The adapter is supposed to be temporary (phase 5 deletes it). If the engine or the VLC renderer grow coupled to the adapter, deletion becomes a refactor we never do. The adapter accretes.

**Mitigation:**

- The adapter is in `WallpaperTurbo.Player.Vlc`, not in `WallpaperTurbo.Core`. The engine is free to forget it.
- `[Obsolete]` is on `IMediaPipeline` from phase 1.
- The `Program.cs` change in phase 5 (delete adapter) is a small, mechanical PR. It is committed to in this spec.
- A grep test in CI fails the build if `IMediaPipeline` is referenced from any file outside `Player.Vlc`.

#### R-A2 (High): Renderer-specific concepts leak into the engine.

**The risk:** The current `HardwareDecodePipeline.ApplyLayoutMode` uses VLC's `AspectRatio` and `CropGeometry` directly. A future engineer "fixes" a layout bug by adding an `IRenderer.SetAspectRatio` method, polluting the contract.

**Mitigation:**

- The contract has a single `ApplyLayout(WallpaperLayoutMode mode)` method that takes the existing `WallpaperLayoutMode` enum (Stretch/Fit/Fill). The enum is the boundary.
- A code review rule: any PR that adds a method to `IRenderer` requires an ADR (`docs/decisions/`) explaining why.
- A grep test in CI fails the build if `LibVLC`, `libmpv`, `mpv_*`, or `libvlc_*` appears in any file under `WallpaperTurbo.Core/`.

#### R-A3 (Medium): Per-monitor mixing is requested mid-phase.

**The risk:** Users ask for "different renderers per monitor" (the disabled `MonitorSessionManager.cs` explored this). The architecture supports it, but the implementation requires a multi-monitor UI. If we are forced to add this in phase 4 or 5, scope creeps.

**Mitigation:** The architecture supports it without code changes to the abstraction. A future renderer registry keyed by `MonitorInfo` (or a per-monitor settings model) is the only addition. The decision to defer (per the user's earlier decision) is reaffirmed here. A future spec can add the multi-monitor factory path without touching the abstraction.

#### R-A4 (Medium): Future renderers (WMF, Web, Image) don't fit the contract.

**The risk:** The contract is built around video renderers. A future "Image renderer" has no `Play`/`Pause` semantics — images are static. A future "Web renderer" has its own lifecycle (URL changes, not file changes).

**Mitigation:** The contract is named `IRenderer` not `IVideoRenderer`. The methods are general enough: `Load(MediaSource)`, `Play`, `Pause`. An image renderer would implement `Play` as "show the image"; a web renderer would implement `Load` as "navigate to URL". This is a stretch but not a break.

If a future renderer cannot fit (e.g., a scene renderer with no clear `Play`/`Pause`), the contract may need a `Playable` marker interface or a v2 contract. This is not in scope for this spec.

#### R-A5 (Low): The `MonitorInfo` type leaks into the renderer.

**The risk:** The `RenderTarget` carries `MonitorInfo`. The renderer's lifecycle is bound to a monitor. A future use case (e.g., preview window that is not on a real monitor) does not have a `MonitorInfo`.

**Mitigation:** `MonitorInfo` is currently used only for log messages and the `Size` field. A future revision can move `Size` (and the bounds rectangle) to a separate `RenderGeometry` record on `RenderTarget`, with `MonitorInfo` optional. This is a phase 7+ concern.

### 7.3 Operational / licensing risks

#### R-O1 (High): LGPL compliance for libmpv.

**The risk:** Per the LGPL and per the architecture report (§10), shipping `libmpv-2.dll` (LGPL build) requires:

1. The DLL is dynamically linked (not static). We do this — `libmpv-2.dll` is a separate file next to the engine.
2. The user is allowed to replace the DLL with a different version. We do this — the file is in the install directory, not inside the engine.
3. The source for the LGPL build (or a written offer) is provided with the distribution. **This is the part we must remember.** A plain binary distribution on the website needs a `NOTICE` or `THIRD_PARTY_LICENSES.txt` listing mpv as LGPL and including the LGPL text or a link to it.

**Mitigation:** Phase 2 ships a `THIRD_PARTY_LICENSES.txt` in the install directory. The license text for mpv (LGPLv2.1) is included. The MPV renderer is built and packaged to honor LGPL.

#### R-O2 (Medium): GPL contamination from MPV's default build.

**The risk:** `mpv` core is GPLv2+ by default. If we ship the default mpv build and dynamically link it, our closed-source app is at the mercy of the GPL terms for the mpv build. The architecture report says the GPL build is fine for closed-source if we treat libmpv as a "system library" (LGPL-style argument), but this is a legal grey area.

**Mitigation:** **We ship the `mpv-dev-lgpl` build only.** This build has `-Dgpl=false` and statically links LGPLv3 ffmpeg. The combination is unambiguously LGPL. The phase 2 PR must include verification that the shipped `libmpv-2.dll` is the LGPL build, not the default.

#### R-O3 (Medium): `libvlc.dll` is LGPL but with some GPL modules.

**The risk:** Per the architecture report (§8.1), some VLC modules are GPLv2. If we statically link them into `libvlc.dll`, our app inherits the GPL terms. The current project dynamically links `libvlc.dll` and `libvlccore.dll` from the `VideoLAN.LibVLC.Windows` NuGet, which is the standard distribution. This is the LGPL-compatible path.

**Mitigation:** The current build path is correct. We keep dynamic linking. The bundled `libvlc.dll` is the one from the NuGet. The phase 0 move of the package reference to `Player.Vlc` does not change the linking model.

#### R-O4 (Low): Telemetry / privacy if we ever collect renderer-choice stats.

**The risk:** A future "send anonymous usage stats" feature may inadvertently log the user's choice of MPV, which combined with other telemetry may identify users.

**Mitigation:** Out of scope for this spec. The current Wallpaper Turbo does not appear to ship telemetry (no references in `Program.cs` or `WallpaperTurbo.Core`). Any future telemetry is a separate decision with its own privacy review.

#### R-O5 (Low): Build size grows with MPV.

**The risk:** The install size grows by ~30 MB (the LGPL mpv DLL).

**Mitigation:** The installer can offer a "VLC only" / "Both renderers" choice. Phase 4 makes this decision.

---

## 8. Success criteria

These are objective, testable conditions for "the architecture is done." Each is a checkbox. The architecture is done when every box is checked.

### 8.1 Phase-0 success criteria (architecture is in place)

- [ ] `IRenderer`, `IRendererCapabilities`, `IRendererFactory` exist in `WallpaperTurbo.Core.Media`.
- [ ] `MediaSource`, `RenderTarget`, `PixelSize` records exist in `WallpaperTurbo.Core.Media`.
- [ ] `RendererRegistry`, `RendererSettings`, `RendererSelector` exist in `WallpaperTurbo.Core.Media`.
- [ ] `VlcRenderer`, `VlcRendererFactory`, `VlcCapabilities` exist in `WallpaperTurbo.Player.Vlc`.
- [ ] `WallpaperTurbo.Core.csproj` does not reference `LibVLCSharp` or `VideoLAN.LibVLC.Windows`.
- [ ] `WallpaperTurbo.Player.Vlc.csproj` references the VLC packages and exports the `VlcRendererFactory` to the engine.
- [ ] `Program.cs` constructs the renderer via the factory, not via `new HardwareDecodePipeline(...)`.
- [ ] A grep for `LibVLC` / `libvlc` / `LibVLCSharp` in `WallpaperTurbo.Core/` returns zero results.
- [ ] The smoke test (`red-leaves.mp4`, `crimson-blind.mp4`, both pause modes) is byte-identical to the pre-refactor build.

### 8.2 Phase-2 success criteria (MPV prototype works)

- [ ] `MpvRenderer`, `MpvRendererFactory`, `MpvCapabilities` exist in `WallpaperTurbo.Player.Mpv`.
- [ ] `WallpaperTurbo.Player.Mpv.csproj` references the chosen MPV bindings package.
- [ ] `libmpv-2.dll` (LGPL build) ships in the install directory.
- [ ] `WallpaperTurbo.exe --renderer mpv` plays `red-leaves.mp4`.
- [ ] All `IRenderer` lifecycle methods work for `MpvRenderer`.
- [ ] No regression in the VLC path.

### 8.3 Cross-cutting success criteria (architecture is "done")

- [ ] **Existing VLC behavior unchanged.** The bundled wallpapers play identically. Memory profile, CPU profile, and Explorer-restart recovery all match the pre-architecture build.
- [ ] **MPV can play wallpapers.** Verified manually on at least one Intel iGPU + one NVIDIA dGPU + one AMD dGPU configuration.
- [ ] **Renderer can be switched via settings.** Editing `settings.json` (or using the future settings UI) switches the renderer on the next launch. No code change, no recompile.
- [ ] **No regression in stability.** The Explorer restart, display change, and crash recovery paths work for both renderers.
- [ ] **No regression in wallpaper embedding.** The HWND parenting, click-through child windows, and z-order enforcement work for both renderers.
- [ ] **No regression in resource cleanup.** The 10-second memory trim, the synchronous shutdown on session end, and the defensive `try/catch` in cleanup all work.
- [ ] **No license violation.** `libmpv-2.dll` is the LGPL build. `THIRD_PARTY_LICENSES.txt` is in the install directory. `libvlc.dll` is dynamically linked from the NuGet distribution.

### 8.4 Negative success criteria (things that should NOT happen)

- [ ] No new files in `WallpaperTurbo.Core` reference `LibVLCSharp` or `libvlc`.
- [ ] No new `IRenderer` method has a renderer-specific concept in its name (no `SetHwnd`, no `SetAspectRatio`, no `SetVo`).
- [ ] No DI / IoC framework is added.
- [ ] No `IMediaPipeline` reference outside `WallpaperTurbo.Player.Vlc` after phase 5.
- [ ] No per-monitor renderer mixing is implemented in phases 0–2.
- [ ] No `Auto` mode is implemented in phases 0–5.
- [ ] The VLC pipeline is not rewritten. The behavior of `VlcRenderer` is the behavior of the previous `HardwareDecodePipeline`, modulo the path-resolution change in R-T1.

---

## 9. Open questions

These are the questions the spec could not answer without a human. They are blockers for the corresponding phase.

### 9.1 Phase-0 blockers

**Q-1.** *Should `WallpaperTurbo.Player.Vlc` and `WallpaperTurbo.Player.Mpv` be loaded as NuGet packages, in-repo projects, or both?* The current scaffold has them as in-repo projects. The `WallpaperTurbo.UI.csproj` references `WallpaperTurbo.Core`. A reasonable pattern: each Player project is a separate assembly loaded by the engine, with a registration step in `Program.cs`. The decision affects how the installer and the WPF UI reference the players.

*Default answer if not corrected:* in-repo projects, registered in `Program.cs`. This is the simplest and matches the existing scaffold.

**Q-2.** *What is the canonical location for `settings.json`?* The spec says `%LocalAppData%\WallpaperTurbo\settings.json`. Confirm or correct.

**Q-3.** *Does the WPF UI (WallpaperTurbo.UI) need a separate settings model, or does it read `RendererSettings` directly?* The spec assumes direct read. Confirm.

### 9.2 Phase-2 blockers

**Q-4.** *Which MPV bindings package?* Options:

- `LibMpv.Client` (community, C# wrapper)
- `Mpv.NET` (community, more featured)
- `mpv.NET` (older, less maintained)
- Direct P/Invoke to `libmpv-2.dll` (no wrapper, maximum control, more work)

*Default answer if not corrected:* **direct P/Invoke.** The MPV API surface is small enough (per the architecture report, ~30 entry points) that a hand-written wrapper is preferable to a third-party dependency we don't control. The phase 2 PR may revisit this if a maintained C# wrapper is preferred.

**Q-5.** *Which version of libmpv?* The architecture report cites various commits. Pin a version in phase 2; the LGPL build is published by `mpv-winbuild` (zhongfly, erickyun, mitsch). Pick one and freeze it.

*Default answer if not corrected:* the most recent stable `mpv-dev-lgpl-x86_64-*.7z` at the time phase 2 begins. Pin by hash.

**Q-6.** *How do we handle the `--vo`, `--hwdec`, `--gpu-api` MPV options in the default config?* The architecture report's §8.3 lists a wallpaper-recommended set. Confirm or correct.

*Default answer if not corrected:* `vo=gpu-next`, `hwdec=d3d11va`, `gpu-api=d3d11`, `gpu-context=d3d11`, `ao=null`, `no-input=yes`, `no-osc=yes`, `loop-file=inf`, `mute=yes`, `wid=<hwnd>`.

### 9.3 Phase-3 blockers

**Q-7.** *What hardware do we benchmark on?* The spec says "at least 3 configs." Confirm the target list.

*Default answer if not corrected:* the team's own workstations plus one borrowed or rented machine per dGPU vendor. Document the configs in `docs/benchmark-results-*.md`.

**Q-8.** *What is "good performance"?* The benchmark records numbers. The "Auto" mode in phase 6 will use them. Define pass/fail criteria.

*Default answer if not corrected:* 1080p playback uses < 5% CPU on a modern iGPU, 4K playback uses < 15% CPU. Working set is < 200 MB per session. First-frame latency is < 1 second. These are aspirational; the real numbers will inform phase 6.

### 9.4 Phase-4 blockers

**Q-9.** *Is the experimental release a public release (downloaded by users) or an internal release (dogfooded by the team)?* The risk profile differs.

*Default answer if not corrected:* dogfood first. A 2-week internal dogfood before public release.

### 9.5 Phase-5 blockers

**Q-10.** *Does the WPF UI already have a settings page?* If yes, this phase is a small addition. If no, this phase is a larger scope.

*Default answer if not corrected:* the WPF UI is a new project; this phase will add a settings page from scratch.

**Q-11.** *When is `IMediaPipeline` deleted?* The spec says phase 5. Confirm.

### 9.6 Phase-6 blockers

**Q-12.** *What is the Auto chain?* Per the architecture report, "try MPV, fallback VLC" is the user-requested default. Confirm.

*Default answer if not corrected:* `["mpv", "vlc"]`. The chain is ordered; the first renderer whose `Initialize` succeeds wins.

**Q-13.** *Is the chain user-editable?* The spec allows it. Confirm or simplify to "fixed chain."

*Default answer if not corrected:* fixed chain in phase 6. User-editable in a later phase.

### 9.7 Cross-cutting blockers

**Q-14.** *What is the rollback story?* The spec says "delete `settings.json`." Confirm.

*Default answer if not corrected:* the default in code is `"vlc"`. Deleting `settings.json` causes the next launch to use the default. This is the rollback.

**Q-15.** *Where does `libmpv-2.dll` live in the install layout?* The spec assumes the same directory as `libvlc.dll` and the engine `.exe`. Confirm.

*Default answer if not corrected:* `%InstallDir%\libmpv-2.dll`. The Win32 DLL search order will find it.

---

## 10. Decision log

| Decision | Outcome | Section |
|---|---|---|
| Renderer abstraction as `IRenderer` (not `IMediaPipeline`) | Accepted | §2 |
| `MediaSource` and `RenderTarget` records | Accepted | §2.4 |
| `IMediaPipeline` is `[Obsolete]` and survives as adapter target | Accepted | §3.6 |
| Settings in `%LocalAppData%\WallpaperTurbo\settings.json` | Default; subject to Q-2 | §3.2 |
| `RendererRegistry` is a static manually-populated list | Accepted | §3.3 |
| `RendererSelector.Resolve` is the only fallback mechanism | Accepted | §3.4 |
| Capability model is data only | Accepted | §4 |
| No DI framework | Accepted | §1.3, §3.3 |
| No per-monitor renderer mixing in initial release | Accepted | §1.3, §5.7 |
| `Auto` mode is phase 6 | Accepted | §6.7 |
| Renderer choice is sticky per session | Accepted | §1.4 |
| `Shutdown` is separate from `Dispose` | Accepted | §2.3, §5.4 |
| `Shutdown` is synchronous; takes up to a frame's time | Accepted | §2.8, R-T6 |
| Engine does not call renderer methods concurrently | Accepted | §2.9 |
| `IRenderer` event surface is `IsPlaying` only (phase 0–2) | Accepted | §2.10 |
| Renderer-specific concepts (MakeChildrenTransparent, etc.) stay in renderer | Accepted | R-T2 |
| MPV uses `--wid` only in phase 2 (no render context) | Accepted | R-T3 |
| Ship `mpv-dev-lgpl` only (not default mpv) | Accepted | R-O2 |
| `libmpv-2.dll` ships next to `libvlc.dll` | Accepted | R-T4, Q-15 |
| 6-phase migration with VLC as default throughout | Accepted | §6 |
| Architecture reviews at phase boundaries (0, 2, 4, 6) | Accepted | §6 |

---

## 11. Final note for the implementing engineer

The architecture's central insight is this: **the existing VLC pipeline is not the problem; the existing pipeline's name is.** The implementation is good. The contract is wrong. Phase 0 of this spec is mostly a rename and a file move, with one substantive change (the path resolution in R-T1). Phases 0 and 1 should be small PRs that a reviewer can sign off on in an afternoon. Phase 2 is the first phase where the codebase has two renderers in it.

The most likely failure mode is **scope creep in phase 0**. The architecture is laid out for phase 6 features (auto, multi-monitor mixing, future renderers). None of those need to be implemented in phase 0. The spec calls this out in the "non-goals" and in the negative success criteria. If a reviewer asks "but what about auto mode?" the answer in phase 0 is "out of scope; the architecture supports it; we will implement it in phase 6."

The second most likely failure mode is **leaking VLC concepts into the engine**. The grep test in R-A2 is the safety net. The contract review at every phase boundary is the second safety net.

If the implementation follows this spec and the spec is read carefully, the result is a stable, extensible renderer architecture that lets Wallpaper Turbo users pick VLC or MPV without anyone having to rewrite working code.
