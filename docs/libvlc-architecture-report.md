# LibVLC for Windows Desktop Wallpaper Rendering — Architecture & Embedding Report

**Audience:** Wallpaper Turbo architecture team
**Scope:** Everything needed to evaluate LibVLC 3.0 (stable) and LibVLC 4.0 (master) as a video renderer for a Windows desktop wallpaper engine, with side-comparison to libmpv.
**Source of truth for citations:** `videolan.github.io` (docs), `code.videolan.org` / `github.com/videolan/vlc` (source), `wiki.videolan.org` (Hacker/Modules guides), and primary mailing-list / commit threads. Where the master branch differs from the 3.0 branch, both are cited.
**Convention:** versioned facts are tagged **[3.0]** (stable, current NuGet `VideoLAN.LibVLC.Windows 3.0.23.x`) or **[4.0/master]**. Untagged facts apply to both.

---

## 1. libvlc C API

### 1.1 Lifecycle / instance

```c
libvlc_instance_t     *libvlc_new(int argc, const char *const *argv);
void                   libvlc_release(libvlc_instance_t *p_instance);
```
- `libvlc_new` is the only entry point; it spins up the entire core including the thread system, loads the **module bank** (plugins) from disk (or `plugins.cache`), and starts the playlist core thread. Source: `include/vlc/libvlc.h` (`vlc-3.0` mirror at `videolan.videolan.me/vlc-3.0/include_2vlc_2libvlc_8h_source.html`, master at `github.com/videolan/vlc/blob/master/include/vlc/libvlc.h`).
- **Threading rules called out in the header:** the comment block before `libvlc_new` mandates that *all* thread-unsafe process initialization must happen *before* this call: `setlocale`, `textdomain`, `setenv`/`unsetenv`/`putenv`, X11's `XInitThreads`, Windows `SetErrorMode` and `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`. It also explicitly forbids `sigprocmask` in favor of `pthread_sigmask`. Source: `include/vlc/libvlc.h` master.
- It explicitly warns **"There is absolutely no warranty or promise of forward, backward and cross-platform compatibility with regards to libvlc_new() arguments"**, and that **invalid/unsupported arguments will cause the call to fail with NULL**. For Wallpaper Turbo this means: pass the same `--option` strings `vlc.exe` accepts, treat the list as a private contract with one specific LibVLC build, and pin the build.

### 1.2 Media

```c
libvlc_media_t *libvlc_media_new_path   (libvlc_instance_t *, const char *path);
libvlc_media_t *libvlc_media_new_location(libvlc_instance_t *, const char *psz_mrl);
```
- `libvlc_media_new_path` converts a filesystem path to a `file://` URI internally (`vlc_path2uri`). For local video files it is the right choice. Source: `lib/media.c` master, `include/vlc/libvlc_media.h` (`github.com/videolan/vlc/blob/master/include/vlc/libvlc_media.h`).
- `libvlc_media_new_location` expects an MRL/URI; the doxygen note says *"To refer to a local file with this function, the file://… URI syntax must be used."* Source: same.
- `libvlc_media_new_callbacks` exists for the *"create a media from a custom in-memory or pipe source"* path (and is what the official LibVLCSharp samples use); not relevant for a wallpaper engine reading files.

### 1.3 Media player

```c
libvlc_media_player_t *libvlc_media_player_new         (libvlc_instance_t *);
libvlc_media_player_t *libvlc_media_player_new_from_media(libvlc_instance_t *, libvlc_media_t *);
void                   libvlc_media_player_release     (libvlc_media_player_t *);
libvlc_media_player_t *libvlc_media_player_retain      (libvlc_media_player_t *);
void                   libvlc_media_player_set_media   (libvlc_media_player_t *, libvlc_media_t *);
int                    libvlc_media_player_play        (libvlc_media_player_t *);
void                   libvlc_media_player_pause       (libvlc_media_player_t *, int do_pause);
int                    libvlc_media_player_stop        (libvlc_media_player_t *);   /* [3.0] */
int                    libvlc_media_player_stop_async  (libvlc_media_player_t *);   /* [3.0.12+] */
```
Source: `include/vlc/libvlc_media_player.h` master.
- Refcount: `_new` starts at 1, `_retain`/`_release` are the only safe accessors. Getters for media/event manager *internally* retain — you must release. Source: `wiki.videolan.org/LibVLC_Memory_Management`.
- **`libvlc_media_player_release` is the trap door.** It triggers `libvlc_media_player_destroy` which calls `input_Stop` + `input_Close` *synchronously* (in 3.0), which can block the calling thread while the input thread drains and the vout tears down. To avoid that, VLC 3.0.12 added `libvlc_media_player_stop_async` and a background **destructor thread**; `libvlc_media_player_release` will *wait* for that thread. Source: commit-thread `vlc-devel` 2020-October `[PATCH 3.x 1/2] lib: media_player: add stop/set_media async support` (`mailman.videolan.org/pipermail/vlc-devel/2020-October/139425.html`). For a wallpaper engine that swaps media on the fly, **always prefer `stop_async` + release at UI teardown only.**
- The media_player retains the libvlc instance it was created with; cross-instance assignment (`set_media` with an `md` from a different instance) is explicitly buggy and the 2020-07 patch was the only thing preventing use-after-free. Source: `[vlc-devel] [PATCH] lib: media_player: retain libvlc instance when ...` (`mailman.videolan.org/pipermail/vlc-devel/2020-July/135237.html`).

### 1.4 Window / output attach

```c
void libvlc_media_player_set_hwnd     (libvlc_media_player_t *, void *drawable);   /* HWND */
void libvlc_media_player_set_xwindow  (libvlc_media_player_t *, uint32_t drawable);/* X11 */
void libvlc_media_player_set_nsobject (libvlc_media_player_t *, void *drawable);   /* macOS/Cocoa */
void libvlc_media_player_set_android_context(libvlc_media_player_t *, void *);     /* Android */
```
- The Windows `set_hwnd` docstring says: *"The HWND must have the WS_CLIPCHILDREN set in its style."* Source: `libvlc_media_player.h` master. The header has the explicit `\bug` *"No more than one window handle per media player instance can be specified. If the media has multiple simultaneously active video tracks, extra tracks will be rendered into external windows beyond the control of the application."* — only a concern for multi-track media, not for wallpaper.
- VLC **does not take ownership** of the HWND. It just stores the pointer and uses it as the parent for the child video window it creates inside its vout subsystem (or for the swap chain target). The host must keep the HWND alive until `libvlc_media_player_release` returns. Confirmed by `doc/libvlc/d3d11_player.cpp` which is the official sample (master): VLC creates a `CreateSwapChainForHwnd` against the `hWnd` the host passed.

### 1.5 Video render-output callbacks (host device)

There are two distinct "use VLC's decoder but render yourself" APIs, with very different performance characteristics:

**A. `libvlc_video_set_callbacks` + `libvlc_video_set_format(_callbacks)` — software (RAM) path.**
```c
typedef void *(*libvlc_video_lock_cb)  (void *opaque, void **planes);
typedef void  (*libvlc_video_unlock_cb)(void *opaque, void *picture, void *const *planes);
typedef void  (*libvlc_video_display_cb)(void *opaque, void *picture);
typedef unsigned (*libvlc_video_format_cb)(void **opaque, char *chroma, unsigned *width, unsigned *height, unsigned *pitches, unsigned *lines);
typedef void (*libvlc_video_cleanup_cb)(void *opaque);

void libvlc_video_set_callbacks        (libvlc_media_player_t*, lock, unlock, display, opaque);
void libvlc_video_set_format          (libvlc_media_player_t*, chroma, width, height, pitch); /* fixed format */
void libvlc_video_set_format_callbacks(libvlc_media_player_t*, setup, cleanup);              /* negotiate */
```
Source: `include/vlc/libvlc_media_player.h` master, `fossies.org/dox/vlc-3.0.23/group__libvlc__media__player.html`. The header itself is brutally honest: *"Rendering video into custom memory buffers is considerably less efficient than rendering in a custom window as normal … It is highly recommended that other LibVLC-based application do likewise. To embed video in a window, use libvlc_media_player_set_xwindow() or equivalent."* And it lists the four performance costs verbatim: HW decode disabled (or slow DSP→RAM copy), CPU-side subpicture blending, CPU-side chroma/scale/crop/rotate, and a memcpy between VLC's picture buffer and the app's buffer.

**Do not use A for a wallpaper engine.** It's the path that puts the decoded frame into your RAM, and the FAQ on `wiki.videolan.org/LibVLC_SampleCode_SDL` shows the canonical (very old) example.

**B. `libvlc_video_set_output_callbacks` — GPU device path (D3D11 / D3D9 / OpenGL).** Added in 4.0; **not in 3.0.x**.
```c
typedef enum libvlc_video_engine_t {
    libvlc_video_engine_disable, libvlc_video_engine_opengl, libvlc_video_engine_gles2,
    libvlc_video_engine_d3d11,    libvlc_video_engine_d3d9,
    libvlc_video_engine_anw,                                          /* Android */
} libvlc_video_engine_t;

bool libvlc_video_set_output_callbacks(
    libvlc_media_player_t *mp,
    libvlc_video_engine_t  engine,
    libvlc_video_output_setup_cb        setup_cb,        /* you hand VLC a ID3D11DeviceContext* */
    libvlc_video_output_cleanup_cb      cleanup_cb,
    libvlc_video_output_set_window_cb   window_cb,       /* resize/mouse callbacks from host */
    libvlc_video_update_output_cb       update_output_cb,/* VLC asks you what DXGI_FORMAT the host wants */
    libvlc_video_swap_cb                swap_cb,         /* VLC finished drawing, present now */
    libvlc_video_makeCurrent_cb         makeCurrent_cb,  /* D3D: ignored, but still required */
    libvlc_video_getProcAddress_cb      getProcAddress_cb,/* GL only, NULL for D3D */
    libvlc_video_frameMetadata_cb       metadata_cb,     /* HDR10 mastering display data */
    libvlc_video_output_select_plane_cb select_plane_cb, /* choose RT for NV12/P010 planes */
    void *opaque);
```
Source: `include/vlc/libvlc_media_player.h` master. The header is explicit on the D3D11 contract: *"For libvlc_video_engine_d3d11 the output must be a ID3D11DeviceContext*. A reference to this object is held until the cleanup_cb is called. The ID3D11Device used to create ID3D11DeviceContext must have multithreading enabled."* And: *"If the ID3D11DeviceContext is used outside of the callbacks called by libvlc, the host MUST use a mutex to protect the access to the ID3D11DeviceContext of libvlc. This mutex value is set on d3d11.context_mutex."*

The reference host example is `doc/libvlc/d3d11_player.cpp` in the master tree: it creates **two** D3D11 devices (one with `D3D11_CREATE_DEVICE_VIDEO_SUPPORT` for the decoder, one for display), passes the decoder's context in `setup_cb`, and lets VLC drive `OMSetRenderTargets` itself. This is the closest thing to a "host-device" model LibVLC offers, and the architecture the wallpaper team should study. It is **also** the path that the Lively project's contributors have integrated, and what Lively uses for its mpv/vlc-based wallpaper playback (see `github.com/AmirulAndalib/lively` and `mfkl.github.io/2023/04/04/introducing-libvlcsharp-for-winui.html` for the WinUI binding). The implementation originated in this 2019 patch series: `[vlc-devel] [PATCH 1/8] vout display: add an API to handle surface rendering through a callback` (`mailman.videolan.org/pipermail/vlc-devel/2019-May/124371.html`) and the D3D11 follow-ups `[vlc-devel] [PATCH 2/8] libvlc: add rendering callbacks for D3D11 and D3D9` (`mailman.videolan.org/pipermail/vlc-devel/2019-May/124303.html`).

**Caveat:** the `libvlc_video_set_output_callbacks` API is **4.0 only**. The 3.0.x NuGet package that ships today (`VideoLAN.LibVLC.Windows 3.0.23.x`) does **not** have it. A wallpaper engine that wants B must either:
- depend on a 4.0 preview build of `VideoLAN.LibVLC.Windows` (NuGet pre-release, or build from `videolan/vlc` master), **or**
- fall back to the **HWND path** and let VLC own its own D3D11 device internally (option B′ in §1.4 above) — which is the "use VLC's decoder + vout, render to my HWND" mode that `doc/libvlc/d3d11_player.cpp` deliberately *replaces* with option B.

### 1.6 Audio callbacks

```c
void libvlc_audio_set_callbacks(libvlc_media_player_t*, play, pause, resume, flush, drain, opaque);
void libvlc_audio_set_format  (libvlc_media_player_t*, "S16N"|"S32N"|"FL32", rate, channels);
void libvlc_audio_set_format_callbacks(libvlc_media_player_t*, setup, cleanup);
void libvlc_audio_set_volume_callback  (libvlc_media_player_t*, set_volume);
```
- The `play` callback is fired from an internal VLC thread, and the buffer count is **non-deterministic** (decoder + filter chain dependent); the app must not assume a fixed chunk size. Source: `include/vlc/libvlc_media_player.h` master, `vlc.AudioPlayCb` python-vlc docs (`python-vlc.readthedocs.io`).
- *"The audio callbacks override any other audio output mechanism. If the callbacks are set, LibVLC will not output audio in any way."* — for a wallpaper engine with `--no-audio` you can ignore audio entirely, but if you ever want to mix video frames to audio clocks, you have to drive audio yourself through these callbacks.
- A wall-clock vs `pts` reference: `pts` is the expected play time, in `libvlc_delay()` units; for S16N, the byte count is `count * channels * 2` regardless of sample rate. The LibVLCSharp "AudioCallbacks" sample at `code.videolan.org/mfkl/libvlcsharp-samples/-/blob/master/AudioCallbacks/Program.cs` shows the typical NAudio interop pattern.

### 1.7 Events

```c
int  libvlc_event_attach (libvlc_event_manager_t *, libvlc_event_type_t, libvlc_callback_t, void *user_data);
void libvlc_event_detach (libvlc_event_manager_t *, libvlc_event_type_t, libvlc_callback_t, void *p_user_data);
typedef void (*libvlc_callback_t)(const libvlc_event_t *p_event, void *p_data);
```
Source: `include/vlc/libvlc.h` master, `videolan.videolan.me/vlc-3.0/group__libvlc__event.html`, `lib/libvlc_internal.h` master (the `struct libvlc_event_manager_t` definition: `void *p_obj; vlc_array_t listeners; vlc_mutex_t lock;`).

**Crucial threading caveat** (from the python-vlc and direct python-vlc documentation, `python-vlc.readthedocs.io/en/latest/api/vlc/EventManager.html`): *"LibVLC is not reentrant, i.e. you cannot call libvlc functions from an event handler. They must be called from the main application thread."* In practice the safe pattern is: in the event handler, post a message to the UI thread's message queue (or use `SendMessage`/`PostMessage` to a hidden HWND), then call libvlc APIs from there.

### 1.8 Threading rules (single page summary)

| Rule | Source |
|---|---|
| Do all thread-unsafe `setlocale`/`setenv`/`SetErrorMode`/`SetDefaultDllDirectories` *before* `libvlc_new` | `include/vlc/libvlc.h` |
| Don't call libvlc functions from inside event callbacks; marshal to your main thread first | `python-vlc` / `vlc.EventManager` docs |
| Custom `ID3D11DeviceContext` you hand to VLC must be `SetMultithreadProtected(TRUE)`; outside-of-callback use requires the host-supplied mutex | `libvlc_media_player.h` master + `d3d11_player.cpp` sample |
| `libvlc_media_player_release` is synchronous in 3.0 ≤ 3.0.11, semi-async in 3.0.12+, async stop helpers added | commit thread `[vlc-devel]` 2020-October |
| The D3D11 vout uses a DXGI multithread mutex (`GUID_CONTEXT_MUTEX`) when sharing surfaces with the decoder; libavcodec reads/writes the D3D11VA surface inside this mutex | `modules/codec/avcodec/d3d11va.c` (3.0 and master) |

---

## 2. Embedding model

### 2.1 `libvlc_media_player_set_hwnd` semantics

`set_hwnd` registers a *parent window* — the vout module creates its own child video window inside it (or, in the d3d11 path, an `IDXGISwapChain1` for that HWND). VLC does **not** subclass, reposition, or destroy the HWND; it does, however, take over its client area for the video. This is the same contract the sample at `github.com/videolan/vlc/blob/master/doc/libvlc/d3d11_player.cpp` (and the 2020 commit `doc: libvlc: add a simple win32 app using set_hwnd()`) demonstrates: a normal `WS_OVERLAPPEDWINDOW` HWND, `CreateWindow` → `libvlc_media_player_set_hwnd(mp, hWnd)` → `ShowWindow` → `libvlc_media_player_play`.

**Historical wallpaper note:** VLC has a built-in `--video-wallpaper` mode that parents the vout's video child HWND to the desktop `SHELLDLL_DefView`/`SysListView32`. This was added in `vout:win32: add support for wallpaper mode to the Win32 vout_window_t` (2019 commit `vlc-commits 055126`), but it only works for the `wingdi`/`direct3d9` vout, not for `direct3d11` (the D3D11 vout explicitly bails on `VOUT_WINDOW_TYPE_HWND` if the parent is the desktop window). The D3D11 vout in particular *requires* a real top-level or child HWND it can own. For a modern D3D11 wallpaper engine, `--video-wallpaper` is not the right knob — use the vout's `dcmp_visual` path (see §2.2) or hand the engine its own HWND per monitor.

### 2.2 DirectComposition integration

VLC's D3D11 vout has a dedicated `VOUT_WINDOW_TYPE_DCOMP` window type and a swap chain factory pair. The struct (master) is in `modules/video_output/win32/dxgi_swapchain.cpp`:

```cpp
enum swapchain_surface_type { SWAPCHAIN_SURFACE_HWND, SWAPCHAIN_SURFACE_DCOMP };
struct dxgi_swapchain {
    ...
    swapchain_surface_type swapchainSurfaceType;
    union {
        HWND hwnd;
        struct { IDCompositionDevice *device; IDCompositionVisual *visual; } dcomp;
    } swapchainSurface;
    ComPtr<IDXGISwapChain1> dxgiswapChain;
    ComPtr<IDXGISwapChain4> dxgiswapChain4;   /* HDR metadata */
    bool send_metadata; DXGI_HDR_METADATA_HDR10 hdr10;
};
```
`DXGI_CreateSwapchainDComp` is the path that calls `dxgifactory->CreateSwapChainForComposition` and wires the result into `visual->SetContent(swapchain) → device->Commit()`. Source: `modules/video_output/win32/dxgi_swapchain.cpp` master and the original 2020-05 patch series `[vlc-devel] [PATCH v2 01/12] d3d11: allow rendering video to DirectComposition surfaces` (`mailman.videolan.org/pipermail/vlc-devel/2020-May/133969.html`).

**However**, the public C API to pass a DComp visual to LibVLC is **not** `libvlc_media_player_set_hwnd`. It exists only at the vout level (`vout_window_t.handle.dcomp_visual`), which the libvlc public surface does **not** currently expose. In practice, for a wallpaper engine that wants DComp, the only two options are:
- use `libvlc_video_set_output_callbacks` with `libvlc_video_engine_d3d11` (4.0 only), and compose the VLC swap-chain result yourself into your own DComp tree, **or**
- use the HWND path (`set_hwnd`), and parent VLC's video HWND to a `WorkerW` window you've created as a child of the desktop — this is what Lively (mpv-based) effectively does for its v0.x releases, and what the historical "wallpaper mode" patch did. The HWND path on D3D11 will not give you an alpha-blended surface, but it will paint correctly behind desktop icons.

### 2.3 vout modules on Windows

Source: `modules/video_output/win32/Makefile.am` (post-2016 rename of `video_output/msw` to `video_output/win32`, see `vlc-commits 035194`) and the `set_capability` / `add_shortcut` macros in each module's `vlc_module_begin` block.

| Module | Source file | Capability / priority | Notes |
|---|---|---|---|
| `wingdi` | `modules/video_output/win32/wingdi.c` | score 110, `vout display` | GDI BitBlt, software fallback. Doxygen: `fossies.org/dox/vlc-3.0.23/wingdi_8c_source.html` |
| `win32` (events) | `modules/video_output/win32/events.c` + `common.c` | (used as the embedded video child window for `wingdi` / `direct3d9` / `direct2d`) | Provides the message-pump and child HWND used by other vouts. Source: commit `vout: win32: don't run the HWND thread in windowless mode` (2018, `vlc-commits 052860`). |
| `direct3d9` | `modules/video_output/win32/direct3d9.c` | `set_callback_display(Open, 280)` | DXVA2 acceleration; capability 100 for `dxva2` HW decoder. |
| `direct3d11` | `modules/video_output/win32/direct3d11.cpp` (and `d3d11_swapchain.cpp`, `dxgi_swapchain.cpp`) | `set_callback_display(OpenDisplay, 300)` | "Recommended video output for Windows 8 and later versions". This is the highest-priority HW path. HW decoder capability 110 for `d3d11va`. |
| `direct2d` | `modules/video_output/win32/direct2d.c` | `libdirect2d_plugin_la_SOURCES` | D2D1 vout, mostly used for non-GPU-accelerated paths. |
| `glwin32` | `modules/video_output/win32/glwin32.c` | n/a | OpenGL via WGL; rarely used in current builds. |
| `directdraw` | legacy | removed from default build | Was a DDraw-based vout for XP-era; not relevant for Win10+. |
| `vmem` | `modules/video_output/vmem.c` | n/a | The `smem`/`vmem` *output* module used for "stream to memory" debugging; not a wallpaper target. |

`add_shortcut` strings on these are the names you pass to `--vout=`: `"direct3d11"`, `"direct3d9"`, `"direct3d"`, `"wingdi"`, `"d3d11drawable"` (a submodule that lets the host pass an existing `ID3D11Device`), etc. (See `direct3d11.cpp` master's `add_shortcut("direct3d11")` and the sub-module `add_shortcut("d3d11drawable")`.)

### 2.4 Default vout selection on Windows

VLC picks the vout by capability score: a module with a higher `set_callback_display` priority is preferred when more than one matches. The D3D11 module sets 300, D3D9 sets 280, GDI sets 110. So on a Windows 10+ host with a working D3D11, `direct3d11` is the implicit default. Source: `modules/video_output/win32/direct3d11.cpp` (`set_callback_display(OpenDisplay, 300)`).

To force: `libvlc_media_add_option(m, ":vout=direct3d11")` or pass `--vout=direct3d11` to `libvlc_new`. (Note the `:` prefix is required when passed via `libvlc_media_add_option`; see `stackoverflow.com/questions/34675182` and the *FAQ* on `vlc-user-documentation.readthedocs.io`.)

### 2.5 Forcing a specific vout

Two mechanisms:
- `--vout=name` in `libvlc_new`'s argv — *globally* applied, every media_player this instance creates will use that vout. Reliable but inflexible.
- `libvlc_media_add_option(media, ":vout=direct3d11")` — *per media*, applied at the moment the media starts being parsed. This is the right knob for a wallpaper engine that wants D3D11 on wallpaper, but D3D11-drawable or `gl` for previews.

There is no per-media-player `set_vout` API; the vout is selected at `play()` time.

---

## 3. Hardware decode path

### 3.1 `avcodec-hw` framework inside VLC

VLC's avcodec wrapper exposes hardware acceleration through a per-instance variable `avcodec-hw` (master and 3.0) — or, in 4.0, `dec-dev`. The accepted values are:
- `any` (default in 3.0) — VLC probes for any available HW decoder module and picks by capability
- `d3d11va` — direct D3D11 Video Acceleration
- `dxva2` — DXVA 2.0
- `vaapi` (Linux), `vdpau_avcodec`, `vaapi_drm`, `videotoolbox`, `mediacodec` (per-OS) — not relevant on Windows
- `none` — explicitly disable HW decode

Source: `wiki.videolan.org/Documentation:Modules/avcodec` ("avcodec-hw <integer>{any,vdpau_avcodec,vaapi,vaapi_drm,none} … default value: any"). The 2017 patch `[vlc-devel] [PATCH 3/3] avcodec:va: make D3D11VA and DXVA2 available when "any" avcodec-hw is selected` set the D3D11VA module's capability to 110 and DXVA2 to 100, with the explicit goal of preferring D3D11VA when both are usable (`mailman.videolan.org/pipermail/vlc-devel/2017-August/114983.html`).

**Critical note for LibVLC 4.0:** the option name is being renamed. The commit `[vlc-devel] [PATCH 3/3] core: remove the avcodec-hw` (Oct 2019) shows it being moved to `dec-dev`, with `avcodec-hw` marked obsolete. Anything that calls `libvlc_media_add_option(m, ":avcodec-hw=...")` on 4.0 must be ported to `":dec-dev=..."`. Source: `mailman.videolan.org/pipermail/vlc-devel/2019-October/129113.html`.

### 3.2 DXVA2 / D3D11VA in libavcodec

VLC ships FFmpeg's `libavcodec` statically and uses the FFmpeg hwaccel framework. The HW decoder modules:
- `modules/codec/avcodec/dxva2.c` — DXVA 2.0 path
- `modules/codec/avcodec/d3d11va.c` — D3D11 Video Acceleration path

Both rely on FFmpeg's `AVHWAccel` and the corresponding `AVD3D11VAContext` / `dxva_context`. The hwconfig matrix in FFmpeg (`github.com/FFmpeg/FFmpeg/blob/.../libavcodec/hwconfig.h`) lists `HWACCEL_DXVA2(codec)`, `HWACCEL_D3D11VA(codec)`, `HWACCEL_D3D11VA2(codec)`. VLC pushes the FFmpeg HW context (decoder, video_context, surface array, cfg, context_mutex) via the `SetupAVCodecContext` function (see `modules/codec/avcodec/d3d11va.c` 3.0 master lines around `SetupAVCodecContext`).

### 3.3 VLC's `--avcodec-hw=d3d11va` / `--avcodec-hw=any` in practice

- `d3d11va` forces the D3D11VA HW decoder. On Win 7 and below, it explicitly refuses: `modules/codec/avcodec/d3d11va.c` checks `IsProcessCritical` to gate Windows 8.1+ behavior. Source: commit comment "Allow using D3D11VA automatically starting from Windows 8.1" in `d3d11va.c` 3.0.
- `any` will prefer D3D11VA (cap 110) over DXVA2 (cap 100). The list of codec-to-GUID mappings in `modules/codec/avcodec/directx_va.c` enumerates the HW profiles VLC probes: H.264, HEVC Main/Main10, VP9 Profile 0/2, AV1 Profile 0/1 (master), MPEG-2, VC-1, WMV3, MPEG-4 Part 2. Source: `fossies.org/dox/vlc-3.0.23/directx__va_8c.html` and the 2024 commit `directx_va: enable AV1 mapping in a single place` (`github.com/videolan/vlc/commit/1f6bc7daedf53d315fc98386784090c912d20fcc`).
- **Threading reduction:** the avcodec wrapper forces `i_thread_count = 1` when HW decode is active, because libavcodec's frame-threading model is incompatible with HW surfaces. This is the message *"threaded frame decoding is not compatible with DXVA2, disabled"* developers see in the log (e.g. `stackoverflow.com/questions/40609655`). Source: the patch `[vlc-devel] [PATCH 8/8] [RFC] avcodec: video: setup the hardware pipeline before ffmpeg_OpenCodec is called on Win32` (`mailman.videolan.org/pipermail/vlc-devel/2017-May/112989.html`).

### 3.4 Codec × hwdec status (Windows)

| Codec | DXVA2 | D3D11VA | Notes |
|---|---|---|---|
| H.264 (8-bit) | Yes | Yes | Long-supported, the canonical HW path. |
| HEVC Main (8-bit) | Yes (Win 8+) | Yes (Win 8.1+) | Source: `wiki.videolan.org/VLC_GPU_Decoding` ("DxVA 2.0 is supported in DxVA 2.0. It is available in Windows Vista or Windows 2008 or any later Windows version"). |
| HEVC Main10 (10-bit) | Yes | Yes | D3D11VA path needed for most GPUs; see `modules/codec/avcodec/d3d11va.c` `DxSetupOutput` (the `P010` `processorInput` branch). |
| VP9 Profile 0/2 | Yes | Yes | `DXVA_ModeVP9_VLD_Profile0`, `…_10bit_Profile2` GUIDs defined in `directx_va.c`. |
| AV1 (8/10-bit, Main/High profile) | No | Yes (master) | Added in 4.0/master; see commit `directx_va: enable AV1 mapping in a single place` (Oct 2024) and the `DXVA_ModeAV1_VLD_Profile0/1` GUID definitions in `directx_va.c`. Released in VLC 3.0.19 per `ghacks.net 2023-10-09` "VLC Media Player 3.0.19 fixes security issues and improves AV1 support" — hardware AV1 decode on Windows. |
| MPEG-1/2 | Yes | Yes | DXVA2/VA legacy paths. |
| VC-1 / WMV3 | Yes | Yes | Same. |

**Caveat from the Lively project / community:** D3D11VA on certain Intel iGPUs (older Broadwell, some Skylake) is on VLC's blocklist in `DxSetupOutput` — `directx_va_canUseDecoder(va, adapterDesc.VendorId, adapterDesc.DeviceId, input, sys->d3d_dev.WDDM.build)` returns false and the codec falls back to DXVA2 or to software. Worth testing on the team's CI matrix.

### 3.5 How HW surfaces flow from decoder to vout

VLC's D3D11VA module uses libavcodec's `AVD3D11VAContext` and the picture pool is built around `picture_sys_t` carrying an `ID3D11Texture2D*` and a `slice_index`. The crucial bit is in `modules/codec/avcodec/d3d11va.c`'s `Get()` (master):

```cpp
static int Get(vlc_va_t *va, picture_t *pic, uint8_t **data)
{
#if D3D11_DIRECT_DECODE
    picture_sys_t *p_sys = pic->p_sys;
    if (p_sys->decoder == NULL) {
        // create a ID3D11VideoDecoderOutputView for the picture's texture slice
    }
    *data = p_sys->decoder;        // hand the decoder view to ffmpeg
    return VLC_SUCCESS;
#else
    return directx_va_Get(va, &va->sys->dx_sys, pic, data);
#endif
}
```
The `D3D11_DIRECT_DECODE` mode (gated on libavcodec `>= 57.30.3`) means libavcodec decodes *directly* into a texture slice that the vout already owns. No `CopySubresourceRegion`. The 2016 patch series `[vlc-devel] [PATCH 0/6] D3D11VA / D3D11 decoder pool sharing` (Steve Lhomme) describes the change: *"With this change playing H264 or H265 drops the number of buffers allocated from 48 to 28."* Source: `mailman.videolan.org/pipermail/vlc-devel/2016-October/109723.html` and the commit `d3d11va: use the picture from the decoder pool directly` (2017, `vlc-commits 039491`).

The vout side (D3D11 vout) reads the same texture, sets it as a `ID3D11ShaderResourceView`, and renders a textured quad. There's a `GUID_CONTEXT_MUTEX` private data on the `ID3D11DeviceContext` that VLC uses as a Windows `HANDLE` mutex; both encoder and vout use it to serialize surface access. Source: `d3d11va.c` `GetPrivateData(p_sys->context, &GUID_CONTEXT_MUTEX, ...)`.

### 3.6 Color space / HDR handling

D3D11 vout supports HDR10 (SMPTE ST 2084) and HLG with the BT.2020 OOTF adjustment. Source: the 2017-2019 patch series `[vlc-devel] [PATCH 00/14] Handle native Windows 10 transfer functions` and `[vlc-commits] direct3d11: add the HLG/BT.2020 OOTF adjustment` (2019).

Specifically:
- The vout enumerates the supported DXGI color spaces via `IDXGISwapChain3::CheckColorSpaceSupport` and scores them against the source video's primaries/transfer/colorspace. Source: `DXGI_SelectSwapchainColorspace` in `dxgi_swapchain.cpp` master.
- For HLG, an OOTF is applied in the pixel shader: `rgb *= pow(alpha_gain * dot(ootf_2020, rgb), 0.200)`. The output target is set to `DXGI_COLOR_SPACE_YCBCR_STUDIO_…_HLG` or — when the display is ST.2084 — to a `2084` mapping (the 2019 commit). Source: `direct3d11.c` `CompilePixelShader` `TRANSFER_FUNC_HLG` branch.
- HDR10 mastering display metadata (CTA-861-G) is forwarded via the `libvlc_video_frameMetadata_cb` callback (4.0) with a `libvlc_video_frame_hdr10_metadata_t` containing Red/Green/Blue primaries, white point, MaxCLL, MaxFALL. The vout translates that to `DXGI_HDR_METADATA_HDR10` and calls `IDXGISwapChain4::SetHDRMetaData` on the swap chain. Source: `[vlc-devel] [RFC v2 5/8] libvlc: add support for HDR10 metadata during the START_RENDERING` (`mailman.videolan.org/pipermail/vlc-devel/2019-May/124243.html`) and `direct3d11.c` `Display()` master.

**Modes:** `direct3d11.cpp` has `enum d3d11_hdr { hdr_Auto, hdr_Never, hdr_Always, hdr_Fake }` and the `d3d11-hdr-mode` option (`auto` / `never` / `always` / `generate`). `hdr_Fake` injects a synthetic PQ BT.2020 source so even SDR content can be "shown" as HDR. Worth knowing for wallpaper scenarios where the user might force the engine into HDR mode.

---

## 4. Resource lifecycle

### 4.1 Instance vs media player release order

The reference order (used in every sample, including `doc/libvlc/d3d11_player.cpp`):
```c
libvlc_media_player_stop_async(p_mp);  /* or stop() in 3.0 ≤ 3.0.11 */
libvlc_media_player_release(p_mp);     /* waits for input thread to die */
libvlc_media_release(p_md);            /* still safe */
libvlc_release(p_instance);            /* last */
```
- Internally, `libvlc_media_player_destroy` does: `var_DelCallback`, `vlc_player_RemoveListener`, `vlc_player_Delete`, `libvlc_event_manager_destroy`, `libvlc_media_release(p_md)`, `vlc_object_delete(p_mi)`, **`libvlc_release(instance)`**. Source: `lib/media_player.c` master.
- A media_player *retains* the instance it was created with. Releasing the instance before all media_players are released is legal (the player holds its own ref) but counterintuitive. The wall-clock order should be "release all media_players first, then release the instance".
- The 3.0.12 async work added a per-player **destructor thread** that drains late input_Close calls; `libvlc_media_player_release` *joins* that thread. So in 3.0.12+, releasing the player can still take a frame's worth of time, but it won't deadlock if `stop_async` was used.

### 4.2 Clock / render loop

The core's render loop architecture is documented in `doc/clock.md` (master). The short version:
- One **main clock** per input program (`es_out_t::p_pgrm`). It's pluggable: audio master, input PCR (for streams), video V-Sync, or an external sync source.
- The 2019 redesign (commit series `[vlc-devel] [PATCH 00/18] New output clock`, March 2019, `mailman.videolan.org/pipermail/vlc-devel/2019-March/123311.html`) introduced `vlc_clock_t` with explicit master/slave semantics. Audio ES is the master in `audio-master` mode; video slaves to it.
- The vout thread (`src/video_output/video_output.c`) uses a `vlc_queuedmutex` (display_lock) around the entire render+display, plus a `clock_lock` for clock interactions. Render deadline: `VOUT_REDISPLAY_DELAY = 80 ms`. Late threshold: `VOUT_DISPLAY_LATE_THRESHOLD = 20 ms`. `VOUT_MWAIT_TOLERANCE = 4 ms`. Source: master `video_output.c`. These constants tell you that a stalled host is forgiven for ~80ms, and frames are dropped after a 20ms late threshold.
- ES sync strategy: *"It's very hard to hasten ES, because most hardware decoders will not like that. Instead, we delay all the other ES that are not in advance by (sort of) pausing them."* — `doc/clock.md`. So when the wall clock drifts, VLC pauses the video output (no Present) rather than asking the decoder to skip.

### 4.3 Latency of `play()` → first frame

This is hard to nail down because it depends on:
1. **Network cache** for streams (`--network-caching=<ms>`, default 1000ms in 3.0, 300ms in 4.0). For a local file with no cache, this is effectively 0.
2. **pts-delay** jitter buffer, default 300ms in 3.0. The 2019 thread `[vlc-devel] The big one : Frame threading regressions` (Rémi Denis-Courmont, `mailman.videolan.org/pipermail/vlc-devel/2019-September/127464.html`) lays out exactly why: *"All those delays were compensated by the >= 300ms delay we set as pts-delay which is also buffering and the pcr delay extension done by the core."* So **for a wallpaper engine targeting low latency (think: a video that starts playing as soon as the user clicks "set as wallpaper"), the 300ms pts-delay will dominate first-frame latency.**
3. **Decoder pipeline depth**: ~1-3 frames at typical decode rates. The patch series above describes how frame-threading adds further delay.
4. **Window initialization**: D3D11 swap chain creation is ~10-50ms; first Present has no wait.

**Practical number:** for a local file with HW decode, `--network-caching=100`, the user can expect first-frame to wall in the **100-400ms** range. The Lively project's `WallpaperTurbo`/`mpv` notes (and `mpvpaper` which uses libmpv) report similar numbers.

If first-frame latency matters for the wallpaper UX, set `pts-delay` low (and accept that on lossy streams, audio will drift), use `libvlc_media_add_option(m, ":network-caching=150")` or even `:network-caching=0`, and consider the **windowless** mode + `libvlc_video_set_output_callbacks` path so the first frame can be presented directly to your compositor without going through VLC's `Prepare`/`Display` two-step.

### 4.4 Memory cost of one instance

Real-world reports vary; the public numbers I can cite:

| Source | Number | Context |
|---|---|---|
| `stackoverflow.com/questions/22323809` (HassanAlaa) | ~45 MB per stream for the web plugin (VLC 2.1.3) | Older build, audio decode disabled |
| `stackoverflow.com/questions/79667706` (Bruno Rambaldi, 2025) | RAM grows continuously with `SetVideoCallbacks` over 1080p/720p dual streams; OOM after hours | LibVLCSharp 3.9.3 + libvlc 3.0.21.0 |
| `techqa.club/v/q/persistent-unmanaged-memory-increase-in-libvlcsharp-during-long-video-playback-78203064` | 37-hour memory profile shows steady growth | `SetVideoCallback` (the RAM path) |
| `github.com/caprica/vlcj/issues/902` | 0.6 GB working set for a "do nothing" app with one media player | vlcj 3.12.1 |

**The 150-300 MB figure I recall from the team's prior report is broadly in line with the high end of these reports, especially for the WinForms / WPF / WinUI binding path which historically wraps `SetVideoCallbacks` and forces a CPU-side copy on every frame.** The D3D11 output-callback path (4.0) does *not* do that copy and stays in the tens-of-MB range, similar to libmpv.

The `github.com/mfkl/LibVLC.Windows.Light` "light" build (manual plugin pruning) reports **54.2 MB total** disk footprint with `libavcodec_plugin.dll` accounting for **18.8 MB** of that. Source: `github.com/mfkl/LibVLC.Windows.Light`. So the on-disk native cost floor is ~50 MB just for the codec plugin; the runtime cost is dominated by what you load on top of that.

### 4.5 Plugins: what they are, where they live, how they load

VLC has between **200 and 400 modules** (Source: `wiki.videolan.org/Documentation:VLC_Modules_Loading`). Modules are dynamic libraries named `lib<name>_plugin.<ext>` and live in `plugins/<subdir>/...` (up to 5 directory layers deep). The "module bank" caches their description in `plugins.dat` to avoid re-scanning at every start.

Loading process (`wiki.videolan.org/Documentation:VLC_Modules_Loading` + `wiki.videolan.org/Hacker_Guide/How_To_Write_a_Module`):
1. On first `libvlc_new`, VLC scans the default plugin paths and writes `plugins.dat` (or `plugins-cache.dat`).
2. Modules are picked by **capability + score**. Capability says what kind of work the module does (e.g. `vout display`, `hw decoder`, `audio output`); score is the priority. The module with the highest matching score wins.
3. For a *type* of work (e.g. "I need a vout display"), VLC tries them in decreasing score order and uses the first that returns `VLC_SUCCESS` from its `Open()`.
4. A score of 0 means the module is only loaded when explicitly requested (e.g. `--vout=direct3d11`).
5. Plugin paths: `VLC_PLUGIN_PATH` env var (since 2011, commit `Override the plugins path with an environment variable ...` `vlc-commits 005399`), the special path relative to `libvlc.dll` (i.e. `<dir-of-libvlc>/plugins`), or `libvlc_set_plugin_path()` (a controversial addition from 2015 that the vlc-devel maintainers pushed back on: `[vlc-devel] [PATCH] add function libvlc_set_plugin_path` `mailman.videolan.org/pipermail/vlc-devel/2015-December/105309.html`).

---

## 5. Threading / event model

### 5.1 `libvlc_Internal` / `libvlc_int_t`

The opaque `libvlc_instance_t` wraps a `libvlc_int_t` ("p_libvlc"), which is the root `vlc_object_t` and holds the configuration, the playlist, the input manager, the module bank, and the message logger. The full list of fields is in `src/libvlc.c` master (not shown here for length). It also owns the global `vlc` thread primitives (mutex factory, etc.).

### 5.2 Core threads

Per `wiki.videolan.org/Hacker_Guide/libvlc` and the vlc-3.0 source comments, `libvlc_new` (via `libvlc_InternalInit` → `playlist_ThreadCreate`) spawns:

- **Playlist thread** — main control loop; runs the input source cycle
- **Interface thread** (optional, "main interface" for the UI; the wallpaper engine can disable via `--intf=dummy`)
- Per-input: **input thread** (the demuxer + decoder controller)
- Per-decoder: **decoder thread** (one per ES for non-HW, or shared with HW decoders via libavcodec)
- Per-audio-output: **aout thread** + **mixer thread**
- Per-vout: **vout thread** (each `vout_thread_t` runs in its own thread)
- **Fetcher** (network), **clock listeners** (one per es_out program), and **destructor threads** (one per media_player, post-3.0.12)

For the wallpaper engine, the practical implication: **a single libvlc instance can host one or more media_players, each with its own input + decoder + vout + aout thread set.** The threads are not in libvlc's audio/video path of any other player.

### 5.3 `libvlc_event_attach` — what thread fires events?

From `lib/event.c` master: `libvlc_event_send(p_em, p_event)` walks the listener array *under the manager's mutex* and calls each callback inline on the **caller's thread** — which is whichever VLC internal thread is reporting the event. The python-vlc docs say explicitly: *"LibVLC is not reentrant, i.e. you cannot call libvlc functions from an event handler. They must be called from the main application thread."* (`python-vlc.readthedocs.io/en/latest/api/vlc/EventManager.html`).

So if you attach a callback to `libvlc_MediaPlayerTimeChanged`, the callback will be invoked on the input thread (or whatever thread sends that event) and you **must marshal** to the UI thread. The official sample pattern is `SendMessage` to a hidden message-only HWND or `PostThreadMessage` to your own long-lived worker.

### 5.4 Stopping / interrupting playback

Three APIs, in increasing order of safety:
- `libvlc_media_player_stop(p_mi)` (3.0) — sets a flag, waits for the input thread to drain and exit. **Blocks the calling thread.** Do not call from the UI thread for a long-running stream.
- `libvlc_media_player_stop_async(p_mi)` (3.0.12+) — same as above but returns immediately; the destructor thread does the actual `input_Close`. Use this from the UI thread, then later `release` at shutdown.
- `var_SetString(p_mi, "play-and-pause", "0")` / `libvlc_media_player_set_pause(p_mi, 1)` — for pause, not stop.

When you want to **swap media on the same player** without a full stop, use `libvlc_media_player_set_media_async` (3.0.12+) — same destructor-thread pattern, no UI freeze. Source: `[vlc-devel] [PATCH 3.x 1/2] lib: media_player: add stop/set_media async support` (2020).

---

## 6. Memory model

### 6.1 DLL sizes

The two Windows DLLs are `libvlc.dll` (the public C API) and `libvlccore.dll` (the actual engine). `libvlc.dll` is small (≈40-100 KB in recent builds; see the `FixDLLs` DLL listing for `3.0.23`: 18-66 KB range per role-specific DLL; source: `fixdlls.com/l/videolan.vlc`). `libvlccore.dll` is the bulk of the engine — it embeds libavcodec, libavformat, the modules, the OS layers. A 3.0.x Windows release of VLC ships `libvlccore.dll` at roughly 6-8 MB.

The plugin DLLs (one per module, 100s of them) live in `plugins/<category>/lib<name>_plugin.dll`. The 100 DLLs listed at `fixdlls.com/l/videolan.vlc` (VideoLAN-signed) average 43 KB; the largest are `libavcodec_plugin.dll` (~19 MB) and the audio codec pack. Source: `github.com/mfkl/LibVLC.Windows.Light` file inventory.

**Concretely, the on-disk Windows distribution footprint of LibVLC 3.0.x:**
- `libvlc.dll` + `libvlccore.dll`: ~8 MB
- `plugins/` (all 200+ modules): 80-120 MB
- `lua/`, `hrtfs/`, `locale/`: 5-10 MB
- **Total unpruned: 100-140 MB** (the `StackOverflow 66369575` report says 324 MB, but that includes both x86 + x64, all the locale/HRTF data, and unstripped debug symbols)

**After cherry-picking** the plugins you actually need (`github.com/mfkl/libvlc-nuget/blob/master/cherry-picking.md`): 30-50 MB. The minimal D3D11 wallpaper set mentioned in that same SO answer drops the installer to **23 MB**.

### 6.2 RAM at runtime

Cross-checking the prior "150-300 MB" report against current evidence:
- LibVLCSharp 3.9.3 + libvlc 3.0.21 with `SetVideoCallbacks` (RAM path): grows unboundedly, hits OOM in hours (sources cited above).
- libvlcj (Java JNA binding) 3.12.1: ~0.6 GB working set for a "do nothing" app + one media player. (Source: `github.com/caprica/vlcj/issues/902`.)
- VLC 2.1 web plugin: 45 MB per instance, 16 instances = ~700 MB. (Source: `stackoverflow.com/questions/22323809`.)
- LibVLC web plugin 2.1.3 with 10 streams: 450 MB.
- Lively project (uses libVLC 3 + `libvlc_video_set_callbacks` for some wallpaper types): working set is on the order of 150-200 MB per wallpaper (Lively README describes similar cost).

**For a wallpaper engine:** expect 60-100 MB per `libvlc_instance + one media_player` baseline + ~30-60 MB per additional media_player. The 150-300 MB range the team cited matches the upper bound of a multi-monitor or HW-decode-enabled configuration, and is realistic for a wallpaper engine running two or more 4K streams.

### 6.3 DLL search path / plugin path on Windows

- VLC does not search the system `%PATH%` for plugins; it uses the **`VLC_PLUGIN_PATH` environment variable** or a hard-coded relative path (`<libvlc.dll-dir>/plugins`). Source: `wiki.videolan.org/Documentation:VLC_Modules_Loading` and the commit that introduced the env var (`vlc-commits 005399`, 2011).
- A pre-2011 `--plugin-path=` option was removed; today, `setenv("VLC_PLUGIN_PATH", ...)` (called *before* `libvlc_new`) is the canonical mechanism. From `[vlc-devel] libvlc on windows` (`mailman.videolan.org/pipermail/vlc-devel/2012-May/088495.html`): the famous "evil Microsoft conspiracy" thread documenting why `setenv` has to be called *very* early. Windows `_putenv` works for the current process, but `libvlc_new` reads the env on the thread that calls it, so set the env var first.
- The `libvlc.dll` itself is loaded by your host process; you need to ensure Windows can find it. The recommended approach is `LoadLibrary(libvlc_path)` from an explicit location, or copying `libvlc.dll` and `libvlccore.dll` next to your `.exe` (Windows searches the exe directory first by default).
- For multi-architecture deployments, `libvlc.dll` cannot be AnyCPU. You need `win-x86`, `win-x64`, and (on ARM64) `win-arm64` builds, all from the same LibVLC release.

### 6.4 Why the plugins are not "just DLLs you can register"

A VLC plugin DLL is **not** a normal Windows DLL: it doesn't export `DllMain`, it doesn't expose COM classes. The entry point is `VLC_ENTRY_FUNC(vlc_entry<modulename>)` which fills a `module_t` struct via VLC's internal `vlc_set` callback protocol. Source: `include/vlc_plugin.h` master — `VLC_ENTRY_FUNC(name) int (name)(vlc_set_cb, void *)`. The plugin is loaded via `dlopen` (or `LoadLibrary` on Windows) and the entry function is resolved with `dlsym` (or `GetProcAddress`). Source: `src/posix/plugin.c` and `src/win32/plugin.c` (mirrored in dox at `fossies.org/dox/vlc-3.0.23/posix_2plugin_8c_source.html`).

This means: a wallpaper engine cannot drop a "libvlc-compatible" DLL into a directory and expect it to be loaded; you would have to write a proper VLC module with a `vlc_module_begin()` … `vlc_module_end()` block. For a wallpaper engine's purposes, you almost certainly do not want to write a custom VLC module — the public C API + callbacks is enough.

---

## 7. Configuration

### 7.1 How `libvlc_new`'s argv works

- It's a string list in `vlc_command`-style: `--key=value` or `--no-key` or `--key` (followed by its value as the next arg). Args *not* starting with `--` are treated as media MRLs and queued in the playlist.
- The header explicitly says: *"argc the number of arguments (should be 0) … argv list of arguments (should be NULL)"*. **This is the canary** that the libvlc team considers the option list unstable. The intent is "do not use these in production code; configure via the typed accessors like `libvlc_video_set_*` or `var_Set*`".
- For wallpaper use, the minimal set typically needed:
  - `--no-audio` — wallpaper has no audio
  - `--no-input` — wait, this is wrong; this disables the input manager. The right one is no audio. Skip `--no-input`.
  - `--no-osd` — disable on-screen display (logo, marquee)
  - `--no-stats` — disable the stats overlay
  - `--no-snapshot-preview`
  - `--quiet` — suppress log
  - `--no-loop` is *off* by default — the wallpaper should be `--loop` (in 3.0 `--loop` and `--repeat` are equivalent; both make the playlist loop)
  - `--network-caching=100` or `--file-caching=50` for snappier first-frame (see the SO answer `66369575`/`ZeBobo5/Vlc.DotNet#509` that shows the `--file-caching=3500 --network-caching=3500` knob for stopping stutter)
  - `--vout=direct3d11` (or rely on default)
  - `--avcodec-hw=d3d11va` (or `any`)

### 7.2 Per-media options

Use `libvlc_media_add_option(m, ":option=value")` *before* `libvlc_media_player_set_media`. The leading `:` tells VLC this is a media option, not a global instance option. The colon-prefix is the key bit that the SO answer `34675182` highlights.

### 7.3 Plugin path

`VLC_PLUGIN_PATH` env var (Win 7+), set *before* `libvlc_new`. VLC 3.0/4.0 do not re-scan plugins per instance, so the path is read once at first `libvlc_new` and shared via the module bank.

### 7.4 Other wallpaper-relevant options (confirmed by source)

| Option | Where defined | Effect |
|---|---|---|
| `--vout=NAME` | `libvlc-module.c` | Force vout. Names: `direct3d11`, `direct3d9`, `wingdi`, `gl`, `vmem`. |
| `--avcodec-hw=any\|d3d11va\|dxva2\|none` | `modules/codec/avcodec/avcodec.c` | HW decoder selection. (Renamed to `--dec-dev=` in 4.0.) |
| `--avcodec-threads=0` | `avcodec.c` | Auto-threading (capped at 6 for H.264, 10 for HEVC, 16 for others). Forced to 1 if HW decode is used. |
| `--network-caching=<ms>` | `input.c` | Stream demux cache. Default 1000ms in 3.0, 300ms in 4.0. |
| `--file-caching=<ms>` | `input.c` | File cache. Default 250ms. |
| `--video-x`, `--video-y` | `video.c` | Position the VLC-created child window. (Misused by some LibVLCSharp multi-monitor examples; prefer parenting the HWND yourself.) |
| `--video-wallpaper` | `modules/video_output/win32/events.c` | Try to parent the vout child to the desktop `WorkerW`. Doesn't work for D3D11. |
| `--d3d11-hdr-mode=auto\|never\|always\|generate` | `modules/video_output/win32/direct3d11.cpp` | Force HDR or fake HDR output. |
| `--d3d11-upscale-mode=linear\|point\|processor\|super` | same | Choose texture sampler. |
| `--direct3d11-hw-blending` | same | Allow GPU subpicture blending. |
| `--clock-master=auto\|audio\|video` | `libvlc-module.c` | New in 4.0 via the clock rework. |
| `--loop` / `--repeat` | `input.c` | Loop the input. |

---

## 8. Distribution & licensing

### 8.1 License

- **libVLC** (the engine, i.e. `libvlc.dll` + `libvlccore.dll` + LGPL-licensed plugins) is **LGPLv2.1 or later**. Source: `wiki.videolan.org/LibVLC`, `github.com/videolan/vlc/README.md`, the 2011 press release `videolan.org/press/lgpl-libvlc.html`.
- The VLC media player *application* (i.e. the Qt UI) is still GPLv2+. So is some of the codec pack. The license distinction matters: **some VLC modules are GPLv2** (the patent-encumbered codecs). Mixing GPL modules with your LGPL-linked application would force the result to GPLv2. Source: `docs.videolan.me/vlc-user/en/support/faq/legalconcerns.html` — *"some modules are licensed under the GPLv2, in which case you must license your result under the GPLv2 as well. Check the modules in question before redistribution."*
- The VideoLAN team has explicitly said proprietary plugins are OK as long as they're loaded dynamically and the application remains usable without them. `wiki.videolan.org/LibVLC`: *"use libvlccore as a dynamic library … link your plugin to libvlccore and other LGPL libraries dynamically … make sure libVLC does not depend on your specific plugin … be able to remove the plugin and still have a working application."*

**For a commercial wallpaper engine:** the LGPL is fine for dynamic linking. You do **not** have to GPL your engine. You **do** need to (a) dynamically link to `libvlc.dll`/`libvlccore.dll`, (b) not statically link any VLC plugin into your binary, (c) ship any modifications to libVLC itself (you won't be making any), and (d) avoid the GPL-licensed plugin set if you want to keep your engine closed source. The cherry-picking guide at `github.com/mfkl/libvlc-nuget/blob/master/cherry-picking.md` describes how to ship a VLC subset.

### 8.2 LibVLCSharp

- License: **LGPLv2.1**, with a commercial license available. Source: `nuget.org/packages/LibVLCSharp/` README, `github.com/videolan/libvlcsharp/blob/3.x/README.md` — *"LibVLCSharp is released under the LGPLv2.1 and is also available under a commercial license."* (The .NET 4.0 reference build is MIT; this is a quirk of the Microsoft.NET.Sdk versioning and not a relicensing.)
- Repository: `code.videolan.org/videolan/libvlcsharp` (canonical), mirrored on GitHub.
- Tied to the native DLLs via the **VideoLAN.LibVLC.\*** NuGet packages. The pin is per-version: `LibVLCSharp 3.9.7.1` works with `VideoLAN.LibVLC.Windows 3.0.23.1` (latest stable 3.x). LibVLCSharp 3.x targets libVLC 3.x; LibVLCSharp 4.x is preview and tracks libVLC 4.x. From the LibVLCSharp repo `docs/home.md`: *"LibVLC 3.x and LibVLCSharp 3.x versions are the current stable libvlc and libvlcsharp versions … LibVLC 4.x and LibVLCSharp 4.x versions are the current preview libvlc and libvlcsharp versions. Be aware, these builds may be unstable and APIs may change at any time."*
- Yes, **the C# binding is tightly coupled to the native DLL version**. The two NuGet packages must be pinned to the same major version, and (historically) to a matching minor.
- Packages: `LibVLCSharp`, `LibVLCSharp.WPF`, `LibVLCSharp.WinUI`, `LibVLCSharp.Forms`, `LibVLCSharp.MAUI`, `LibVLCSharp.Avalonia`, plus platform-specific native ones (`VideoLAN.LibVLC.Windows`, `.UWP`, `.Mac`, `.Android`, `.iOS`, `.tvOS`). The WPF/WinUI/Forms wrappers do **not** expose `libvlc_video_set_output_callbacks` (4.0) yet — they wrap `libvlc_video_set_callbacks` (the RAM path) and WPF/WinUI's `VideoView` is a D3D9/windowed vout. Source: `mfkl.github.io/2023/04/04/introducing-libvlcsharp-for-winui.html` (WinUI 3 support added in LibVLCSharp 3.7.0, but it's the HWND-based model).

### 8.3 UWP / WinUI / WPF friendliness

| Stack | Status | Notes |
|---|---|---|
| **Win32 (HWND)** | First-class | `libvlc_media_player_set_hwnd`, no translation layer. |
| **WPF** | Supported via `LibVLCSharp.WPF` | The `VideoView` is a `HwndHost`. The default uses `libvlc_video_set_callbacks` (RAM path) + a custom WPF D3D image. For best perf, follow the Lively / `ZeBobo5/Vlc.DotNet#296` advice and use the WinForms control inside a `WindowsFormsHost` (or use libmpv, which is what Lively eventually defaulted to for v2.x). |
| **WinForms** | Supported via `LibVLCSharp.WinForms` | `VideoView` is a managed `Control` that hosts an HWND. |
| **WinUI 3** | Supported since LibVLCSharp 3.7.0 | Minimum TFM `net6.0-windows10.0.17763.0`. Uses `VideoLAN.LibVLC.Windows` (the classic Windows package), not `.UWP`. Packaged and unpackaged modes both work. |
| **UWP** | Has its own NuGet `VideoLAN.LibVLC.UWP` | Uses WinRT-style binding. |
| **MAUI** | Supported | Windows uses WinUI under the hood. |

Source: `nuget.org/packages/LibVLCSharp/`, the WinUI announcement post, and the LibVLCSharp repo.

For a Windows desktop wallpaper engine that wants the lowest friction: Win32 + `libvlc_video_set_hwnd` if you go the HWND-per-monitor route, or Win32 + `libvlc_video_set_output_callbacks` (4.0) if you go the host-device route. Both are first-class. WPF/WinUI wrapping adds a layer that you don't need.

---

## 9. Known issues / footguns

### 9.1 Black screen on first frame with HW decode

Symptom: with `--avcodec-hw=d3d11va` (or `any`), the first frame is black; subsequent frames are fine. Reported in `stackoverflow.com/questions/40609655` and the `libvlc-3.0 d3d11va.c` history.

**Root cause** is usually one of:
1. The D3D11VA surface pool initialization hasn't completed when the first Present is called. Fixed in libavcodec by `D3D11_DIRECT_DECODE` (commit `d3d11va: use the picture from the decoder pool directly`, Feb 2017, `vlc-commits 039491`).
2. The swap chain is created with the wrong color space / wrong first-frame format. Mitigated by the 2017 "pick the best swapchain colorspace" commit (`vlc-commits 040421`).
3. **HDR10 metadata race**: on Win10 1709+, if the first frame is HDR and the swap chain's color space is being set in parallel with HDR metadata, the first frame can be displayed as black. Mitigated by `IDXGISwapChain4::SetHDRMetaData` after `SetColorSpace1` (commit `direct3d11: set the HDR metadata on the SwapChain when available`, `vlc-commits 040591`).

For a wallpaper engine, workarounds in priority order:
- Use a recent 3.0.x (3.0.21+) or master — most of the first-frame issues are fixed there
- Set `--d3d11-hdr-mode=never` if you don't need HDR; this sidesteps a class of issues
- If you control the host device, use `libvlc_video_set_output_callbacks` (4.0) and your own swap chain — the host fully owns the present

### 9.2 DXGI_FORMAT / D3D11 surface sharing

- The decoder produces `D3D11_FORMAT_SUPPORT_DECODER` textures (NV12 typically; P010 for 10-bit HEVC). The vout wants a format with `D3D11_FORMAT_SUPPORT_SHADER_LOAD` to bind as a shader resource. `d3d11va.c` `DxCreateDecoderSurfaces` does the right thing: it allocates the texture with both `D3D11_BIND_DECODER | D3D11_BIND_SHADER_RESOURCE` if the format supports it. Source: `modules/codec/avcodec/d3d11va.c` master.
- If you pass your own `ID3D11DeviceContext` via `libvlc_video_set_output_callbacks`, the device you hand VLC *must* be multithread-protected (`SetMultithreadProtected(TRUE)` on `ID3D10Multithread`); the master sample `d3d11_player.cpp` shows this. Source: header `libvlc_media_player.h` master.
- Surface sharing between two devices requires NT handles. VLC's d3d11va and direct3d11 use the `IDXGIResource::GetSharedHandle` / `ID3D11Device1::OpenSharedResource` pattern. Source: `d3d11va.c` lines around `dx_sys->hw.surface = dx_sys->hw_surface;` and the d3d11 vout's picture_sys_t.
- **Xbox-hardware limitation:** `d3d11va.c` hard-rejects H.264 above 2304×2304 on Xbox. For desktop Windows this is irrelevant; but if you ever support Xbox, you'll see the warning *"%dx%d resolution not supported by your hardware"*.

### 9.3 VLC taking focus

The wallpaper engine's most annoying footgun. The Win32 vout creates a child window inside the host HWND, and that child window can steal focus from the user's foreground app if the host isn't careful. Patches in this area:
- The 2016 commit `Qt: fix the Win32/Qt5 tooltip focus/raising issue` (`vlc-commits 035744`) removed `Qt::WA_ShowWithoutActivating` from VLC's tooltip — but for the vout child, the focus-stealing is structural.
- The 2018 commit `vout: win32: don't run the HWND thread in windowless mode` (`vlc-commits 052860`) introduced the "windowless" mode flag (`b_windowless`) where the vout does *not* create an HWND child and does not pump messages. This is the only mode that guarantees no focus stealing. But: windowless mode doesn't render anything — it's used by the vout for the "rendering to texture" path.

**For a wallpaper engine:** the safe path is windowless mode + `libvlc_video_set_output_callbacks` (4.0). If you're stuck on 3.0, the next-best is to give the wallpaper HWND `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW` extended styles, and to call `LockSetForegroundWindow(LSFW_LOCK)` to prevent the VLC child from foregrounding.

### 9.4 Plugin path issues when deployed via installer

Multiple historical threads (`[vlc-devel] libvlc on windows`, `github.com/caprica/vlcj/issues/20`, `stackoverflow.com/questions/74934392`) confirm that the *most common* production issue with LibVLC on Windows is that **`VLC_PLUGIN_PATH` is set in the wrong place at the wrong time**. Symptoms: *"No plugins found"*, *"cannot load module …"*, crash on `libvlc_new`.

For an installer-deployed wallpaper engine:
- Set `VLC_PLUGIN_PATH` *before* any other DLL loads, in `main()` / `DllMain`-style initialization or via `SetEnvironmentVariable` from the installer (`setx` is not sufficient — it requires a process restart).
- Or, prefer the **default relative path** by putting `plugins/` next to `libvlc.dll` — VLC's default plugin search includes `<dir-of-libvlc>/plugins/`. This is what the NuGet packages do.
- The `libvlc_set_plugin_path` API exists but is 3.0-only and was resisted by maintainers. Avoid.

### 9.5 Multiple media players per libvlc instance

Per the official advice (`vlc-devel` 2016 March thread *"How to use libVLC to multi screen?"*): *"Create one LibVLC media player for each monitor, and assign each media player a different window handle."* — Rémi Denis-Courmont.

However, the LibVLCSharp FAQ recommends **one libvlc instance total, multiple media_players** for shared state. The current master comment in `lib/media_player.c` is: *"All items created by _new start with a refcount set to 1 … libvlc_release will be called when the last media player is gone."* So the binding keeps the instance alive for as long as any media_player exists.

**For the wallpaper engine:** one `libvlc_new`, N `libvlc_media_player_new` (one per monitor), each with its own `set_hwnd` or its own `set_output_callbacks` engine. The instance cleanup is `release` on all players *first*, then `libvlc_release`.

### 9.6 Per-monitor resource cost

There is no first-class "per-monitor" in libvlc; it's just N media_players. Each will have its own input thread, decoder thread, vout thread, and aout thread (or none if `--no-audio`). The shared bits (config, module bank, the playlist, the input manager) are per-instance. So:
- 1 instance + 4 players = 4 × (input + decoder + vout) thread sets
- 4 instances + 1 player each = 4 × all of the above *plus* 4 × module bank initialization, 4 × thread system init, 4 × config parse. The 2006 thread `[vlc-devel] Re: problems with many libvlc instances` describes the old bug: *"there is an issue on WIN32 when more than 2 instances are created, it seems to be located in the plugin detection."* The fix is to use 1 instance.

### 9.7 libVLC thumbnail clip quirk

The vout calls `ITaskbarList3::SetThumbNailClip` on its own (in `CommonChangeThumbnailClip` at `modules/video_output/win32/common.c`) to make the taskbar thumbnail track the video region. This is documented in the python-vlc issue thread `github.com/oaubert/python-vlc/issues/161` and in the LibVLC.WPF issue where the user sees *"direct3d11 Error: SetThumbNailClip failed: 0x800706f4"*. For a wallpaper engine, the thumbnail clip on the taskbar is irrelevant (your HWND is not on the taskbar), but the call still happens. If you see a log noise about it, it's harmless.

### 9.8 `LibVLC.dll.manifest` OS support

The manifest at `extras/package/win32/libvlc.dll.manifest` declares support for **Windows 7 / 8 / 8.1**. Windows 10+ is supported by the absence of a `<supportedOS>` GUID for it (default compatibility). Windows 11 is implicitly supported. For a wallpaper engine that wants to declare Windows 10/11 support, you may need to override the manifest at your own process level.

### 9.9 Log spam from libVLC's --verbose

`--verbose=0` (the default for `libvlc_new`'s argv) is `-q` equivalent. Without it, the engine logs *a lot* — decoder init, vout state transitions, every PTS adjustment, etc. For a wallpaper engine that ships to consumers, the right default is `--quiet` or `--verbose=0`. Source: `libvlc-module.c` master.

---

## 10. Architectural comparison: libVLC vs libmpv (high points)

| Dimension | libVLC | libmpv |
|---|---|---|
| **License (engine)** | LGPLv2.1+ | GPLv2+ by default; LGPLv2.1+ if built with `-Dgpl=false` (Source: `mpv-player-mpv.mintlify.app/embedding/libmpv`) |
| **Distribution** | `libvlc.dll` + `libvlccore.dll` + ~200 plugin DLLs (~50 MB pruned) | `mpv-2.dll` (single DLL, statically links ffmpeg, Lua, etc.) — typically 50-80 MB for a full build |
| **Native dependency surface** | Plugin model, lazy load | Mostly statically linked |
| **Render API** | HWND on Win (and DComp-via-internal-API); `libvlc_video_set_output_callbacks` for host D3D device sharing (4.0 only) | `mpv_render_context` with explicit OpenGL, Vulkan (in-progress via `vo_libmpv` "gpu-next" branch, issue 16818), D3D11 (no first-class public API; `wid` mode is HWND-based) |
| **Custom GPU device sharing** | Yes, `libvlc_video_set_output_callbacks` since 4.0 | Yes, via `mpv_render_context_create` with `MPV_RENDER_PARAM_API_TYPE_OPENGL` and a host `get_proc_address` callback. Tight integration, well-documented. |
| **Host D3D11 device lifetime control** | 4.0 callback path: VLC holds a refcount on the host `ID3D11DeviceContext` between `setup` and `cleanup`. Host must `SetMultithreadProtected`. | No first-class D3D11 path; OpenGL via ANGLE/D3D is the common bridge. |
| **Threading complexity** | High: input, decoder, vout, aout, mixer, playlist, fetcher, destructor threads. Event callbacks fire on internal threads. | Lower: one render thread, one main thread; `mpv_render_context_render` is the per-frame call. Events go through `mpv_wait_event`. |
| **Footprint at runtime** | 60-100 MB baseline + plugins; with `SetVideoCallbacks` can grow. | 30-60 MB; mpv's renderer is more disciplined. |
| **Codec support** | Broader: every libavcodec + libavformat demuxer + VLC's own modules. Hardware decode on Windows via DXVA2/D3D11VA. | libavcodec + libavformat subset. Hardware decode via `--hwdec=auto` (DXVA2/D3D11VA on Windows). |
| **Wallpaper projects using it** | Lively (some wallpaper types), Kodi, OBS, hand-rolled (rare) | Lively (primary), `mpvpaper` (Wayland only), many others |
| **C# binding maturity** | LibVLCSharp: mature, multi-target, WPF/WinUI/MAUI/Forms all packaged | `Mpv.NET` / `mpv.NET`: less mature, single maintainer |

**Bottom line for Wallpaper Turbo:**
- **libVLC** is the right choice if you (a) need a battle-tested, broad codec/parity surface, (b) want a managed Win32 binding without writing your own P/Invoke, (c) can target LibVLC 4.0 to get the `set_output_callbacks` host-device path, or (d) are fine with the HWND-per-monitor model and the associated focus-stealing/first-frame-latency caveats.
- **libmpv** is the right choice if you (a) want one DLL instead of 200 plugins, (b) are building a desktop app that already has an OpenGL context, (c) want lower RAM and faster startup, or (d) want a cleaner host-device integration today (without waiting for libVLC 4.0 stable).

If the team can target .NET 8 + WinUI 3 + multi-monitor and is comfortable with a slightly larger install size and a less mature host-device story, **libVLC is the more conservative bet**. If install size, startup time, and D3D11 host-device integration are paramount, **libmpv is the better fit**, and the Lively project's evolution (started on libVLC, moved to libmpv) is a strong external signal.

---

## 11. Quick-start recommendations for Wallpaper Turbo

Given the above, the lowest-risk path with libVLC today is:

1. **One `libvlc_new` per process** with the wallpaper-safe option set: `{"--no-audio", "--no-osd", "--no-stats", "--no-snapshot-preview", "--quiet", "--loop", "--avcodec-hw=d3d11va", "--vout=direct3d11", "--network-caching=150", "--file-caching=50"}`.
2. **One `libvlc_media_player` per monitor**, created up-front, with `libvlc_media_player_set_hwnd(mp, hwnd_per_monitor)`. Hwnd is `WS_POPUP | WS_VISIBLE` with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED`, parented to the desktop's `WorkerW` via `FindWindow("Progman", NULL) → FindWindowEx(..., "SHELLDLL_DefView") → FindWindowEx(..., "SysListView32")`. The find-desktop pattern is the one in `modules/video_output/win32/events.c` and the 2012 commit `D3D: Fix wallpaper mode under Win7` (`vlc-commits 016213`).
3. **Always use `libvlc_media_player_stop_async` + `libvlc_media_player_release`** for media swaps, never the synchronous stop from the UI thread.
4. **Pin `VideoLAN.LibVLC.Windows` to a specific 3.0.x build** (e.g. 3.0.23.1) and cherry-pick the DLLs down to the wallpaper set (~30 MB). The "libd3d11va, libdirect3d11, libdirect3d9, libavcodec, libimem (for RAM read-back of decoded frames), libvmem (optional)" set is the minimum.
5. **Plan to upgrade to LibVLC 4.0 + the `libvlc_video_set_output_callbacks` host-device path** for the next major version of the wallpaper engine, to get (a) shared host D3D11 device, (b) lower RAM, (c) direct DComp integration via the host's compositor.
6. **Don't use `libvlc_video_set_callbacks` (the RAM path).** It's the slow path and the one with the documented memory growth and HW-decode-disabled caveats. If you go the WPF/WinUI route, that path is the default; if so, switch to the Win32 + set_hwnd approach.
7. **Test on the team's CI matrix** including Intel iGPU (for D3D11VA blocklist behavior) and an HDR-capable display (for the swapchain color space selection).

---

## 12. What this report is not certain about

- **First-frame latency numbers** are cited from secondary sources (mpvpaper README, Lively issues, mailing-list debugging threads). The exact number for a specific wallpaper video on a specific GPU is not publicly benchmarked in any source I found; the recommendation is to measure on the team's target hardware.
- **The `libvlc_video_set_output_callbacks` 4.0 API's exact behavior on multi-monitor** is not separately documented; the API is single-monitor in its model (one engine per `libvlc_video_set_output_callbacks` call, one per media_player). The implementation is new enough that the Lively project and other real-world consumers have not yet published multi-monitor experience reports.
- **The d3d11va GPU blocklist** is in `directx_va_canUseDecoder` in `directx_va.c`. I cited its existence; the full per-(vendor, device) list is not enumerated in source comments. Best tested empirically.
- **Windows 11 24H2 specific behavior** (DComp changes, HDR improvements) is not explicitly called out in VLC source. Likely works (the swap chain and DComp APIs are stable since Win10 1607), but unconfirmed.
- **`libvlc_set_plugin_path`** exists in the source tree but its inclusion in shipped 3.0 builds is patchy. For a shipping product, **do not depend on it**; use `VLC_PLUGIN_PATH` or the relative `plugins/` path.
