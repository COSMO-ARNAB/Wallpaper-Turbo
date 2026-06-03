# libmpv Architecture & D3D11 Integration — Fact-Grounded Report

> Audience: senior rendering-engine architect evaluating libmpv as the video backend for a Windows desktop wallpaper engine, with LibVLC as the alternative.
> Scope: API surface, embedding model, D3D11/Vulkan hwdec, threading, lifecycle, configuration, distribution, and known footguns.
> Sources: official `mpv-player/mpv` source (master / various commits), `mpv.io` manual, the `mpv_render_context` source in `video/out/vo_libmpv.c`, and discussion threads. Where a claim is uncertain, it is flagged explicitly.
> Caveat: mpv is a fast-moving project; check `git log` on `mpv-player/mpv` near the version you ship. As of January 2026 the render API is OpenGL-only at master; the D3D11 render backend is a long-standing open issue (#5979) and an in-progress `gpu-next` render API (PR #16818, draft).

---

## Executive summary (the parts that change the design)

1. **The official `libmpv` render API supports only two backends: `MPV_RENDER_API_TYPE_OPENGL` and `MPV_RENDER_API_TYPE_SW`.** There is no upstream `MPV_RENDER_API_TYPE_D3D11` (issue #5979 is open since 2018; PR #12627 was closed in 2023; PR #16818 adding a `gpu-next` render backend is a draft, OpenGL-only). A community gist + closed PR expose a `MPV_RENDER_API_TYPE_DXGI` extension that takes `ID3D11Device*` + `IDXGISwapChain*` from the host, but it is **not in master and not supported by mpv devs**. See §2.5.
2. **The supported path to render mpv video into a host-owned D3D11 surface on Windows is `--wid=<HWND> + --vo=gpu-next + --gpu-api=d3d11`.** mpv creates its own swapchain on the host window, draws into it, and your app owns the parent HWND. There is also a merged composition mode (`--d3d11-output-mode=composition`, PR #16285, July 2025) that exports the swapchain to the host, but it is not yet wired to the libmpv render API.
3. **Direct rendering to a host-owned D3D11 texture (no intermediate window) is not currently possible through the official API.** It has been a multi-year open feature request and is the #1 reason a wallpaper engine might prefer LibVLC's `IMemoryNative` / DComp direct path, *if* it needs pixel-level composition with arbitrary scene graph.
4. **mpv runs its own decoder, scheduling, and video-output threads.** The host's threading responsibility is narrow: own the render thread, drive `mpv_wait_event()` and `mpv_render_context_render()` from a deterministic place, and obey an explicit lock-hierarchy. Threading is well-specified and forgiving for a non-render-thread host. See §4–§5.
5. **Licensing: mpv core is GPLv2+ by default; an LGPLv2.1+ build is published as `mpv-dev-lgpl-*.7z` by mpv-winbuild, statically linking LGPLv3 ffmpeg.** There is a stable ABI (`libmpv-2.dll` / `mpv-2.dll`). NuGet packages exist (community). See §10.

---

## 1. libmpv API surface

### 1.1 Two layers

libmpv exposes two distinct APIs ([mpv.io manual — libmpv](https://mpv-player-mpv.mintlify.app/embedding/libmpv), [`libmpv/client.h`](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h)):

1. **Client API** (`libmpv/client.h`): control mpv — create an instance, set options, send commands, observe properties, receive events, terminate.
2. **Render API** (`libmpv/render.h` + backend headers): integrate mpv's video renderer with a graphics API. Allows the host to own the swapchain / GL context / D3D11 device and have mpv render into it.

In addition there is a `stream_cb` API for feeding mpv custom IO, and a deprecated `opengl_cb.h` API superseded by `render.h`. See [mpv-examples/libmpv/README.md](https://github.com/mpv-player/mpv-examples/blob/master/libmpv/README.md).

### 1.2 `mpv_handle` and lifecycle primitives

| Function | Purpose | Source |
| --- | --- | --- |
| `mpv_create()` | Create an uninitialized instance + main client handle. Spawns mpv's core thread. Returns NULL on OOM or if `LC_NUMERIC != "C"`. | [client.h](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h) |
| `mpv_initialize(mpv)` | Finalize setup; config files may load here if `config=yes` is set; also applies `libmpv` profile defaults (no terminal, no config, idle=active, no input cursors). | same |
| `mpv_set_option_string(ctx, name, value)` | Set options **before** `mpv_initialize` (required for a small list) or anytime for most options. | same |
| `mpv_command(ctx, args[])` | Send a command (e.g. `loadfile`, `set`, `cycle`). Blocking, returns an error code. | same |
| `mpv_command_async(ctx, userdata, args[])` | Asynchronous variant; reply delivered as `MPV_EVENT_COMMAND_REPLY`. | same |
| `mpv_get_property` / `mpv_set_property` | Read/write a typed property (`MPV_FORMAT_*`). | same |
| `mpv_observe_property(ctx, userdata, name, format)` | Subscribe to property change events; delivered as `MPV_EVENT_PROPERTY_CHANGE`. | same |
| `mpv_wait_event(ctx, timeout)` | Block for the next event. `timeout=0` polls; `-1` blocks indefinitely. | same |
| `mpv_set_wakeup_callback(ctx, cb, d)` | Get a callback when events are available, instead of polling. | same |
| `mpv_get_wakeup_pipe(ctx)` | Get a non-blocking pipe FD for `select()`/`epoll()` integration. | same |
| `mpv_destroy(ctx)` | Detach this handle. Core dies when last handle is gone. | same |
| `mpv_terminate_destroy(ctx)` | Send `quit`, block until core is fully down, then destroy. Since API 1.29 it blocks. | same |

`mpv_handle` is an opaque struct ("Client context used by the client API. Every client has its own private handle." — [client.h:250](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h)).

### 1.3 `mpv_create` vs `mpv_create_client`

- `mpv_create()` allocates a new mpv **core** (a new `MPContext`), spawns the core thread, and returns a `mpv_handle` for the "main" client.
- `mpv_create_client(ctx, name)` and `mpv_create_weak_client(ctx, name)` create an **additional** handle that shares the same core. Each gets its own event queue, observed-property set, log-message subscription, and async-request state. With `weak`, the core is destroyed once no non-weak handle remains; useful for "subscriber" clients.

Source: [client.h `mpv_create_client` docs](https://mpv.dpldocs.info/~master/mpv.client.mpv_create_client.html), [`player/client.c::mpv_create` / `mpv_create_client`](https://github.com/mpv-player/mpv/blob/master/player/client.c).

For a wallpaper engine you almost always want exactly one `mpv_handle` from `mpv_create` plus one `mpv_render_context`. Multiple clients are for cases like "rendering thread + IPC thread + Lua thread" all sharing the same player.

### 1.4 Thread safety of the client API

From `client.h` "Multithreading" section ([source](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h)):

> The client API is generally fully thread-safe, unless otherwise noted. Currently, there is no real advantage in using more than 1 thread to access the client API, since everything is serialized through a single lock in the playback core.

Specific rules:
- **One thread at a time may call `mpv_wait_event()` on a given handle.** ([mintlify libmpv docs](https://mpv-player-mpv.mintlify.app/embedding/libmpv))
- The wakeup pipe and wakeup callback are reentrant-safe.
- A few functions are annotated "Safe to be called from mpv render API threads" in `client.h` (e.g. time helpers); treat any other client API call as render-thread-unsafe.
- The handle is **not** guaranteed to be safe for concurrent access *before* `mpv_initialize`. ([`mpv_create` docs](https://mpv.dpldocs.info/~master/mpv.client.mpv_create.html))

Locking hierarchy is documented in [`player/client.c`](https://github.com/mpv-player/mpv/blob/master/player/client.c):

```
MPContext > mp_client_api.lock > mpv_handle.lock > * > mpv_handle.wakeup_lock
```

For practical purposes: have a single "control" thread doing all `mpv_command` / property I/O; that thread can be the same thread running `mpv_wait_event`. Use a separate render thread only for the render API (§5).

### 1.5 `mpv_render_context` — when do you need it?

`mpv_render_context` is an opaque struct created by `mpv_render_context_create()`. It is required **only if you want mpv to render video into a graphics object your application controls** (GL FBO, DXGI swapchain, software buffer, …). It is *not* required if you give mpv a window handle (`--wid=<HWND>`) and let it own its own rendering — but it *is* required if you want to do composition, screen capture, custom OSD, or render mpv into a texture you later composite elsewhere.

In other words:
- `wid` only → no render API needed; mpv manages everything.
- custom render path → you need a `mpv_render_context` bound to the `mpv_handle`.
- Currently only OpenGL and software backends are upstream ([`render.h`](https://github.com/mpv-player/mpv/blob/master/libmpv/render.h)).

Source: [`render.h` overview](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html), [`mpv_examples README`](https://github.com/mpv-player/mpv-examples/blob/master/libmpv/README.md).

---

## 2. Embedding model — `mpv_render_context` and `mpv_render_param`

### 2.1 The `mpv_render_param` pattern

Almost all render-API functions take a `NULL`-terminated array of key/value pairs. Keys are `MPV_RENDER_PARAM_*` enum values, values are typed `void*` pointers to typed structs. The macro `MPV_RENDER_PARAM_INVALID == 0` terminates the array. ([`render.h`](https://github.com/mpv-player/mpv/blob/master/libmpv/render.h))

### 2.2 Embedding via `wid` (the "give mpv a window" model)

Set the `wid` option to a `int64_t` cast of a native window handle. mpv creates a child window inside that HWND (or uses it as a parent) and renders. On Windows, you set it like:

```c
int64_t wid = (intptr_t)hwnd;
mpv_set_option(mpv, "wid", MPV_FORMAT_INT64, &wid);
```

The `wid` flow is fundamentally **window-embedded**, not **texture-embedded**: mpv owns a DXGI swapchain on that HWND. The host is a parent window or container — it does not get a texture handle it can composite freely. Compositing is done by Windows (z-order, transparency, etc.).

> "You can output and embed video without this API by setting the mpv 'wid' option to a native window handle… In general, using the render API is recommended, because window embedding can cause various issues, especially with GUI toolkits and certain platforms." — [`render.h`](https://github.com/mpv-player/mpv/blob/master/libmpv/render.h)

For a wallpaper engine specifically, the `wid` path is a real option (give mpv the worker-W HWND of the desktop, no render API needed). The trade-off: you cannot get a D3D11 texture to mix with your own scene. You can only control where the window sits in z-order.

### 2.3 OpenGL render API (the only fully supported one)

To use the render API, pass:

```c
mpv_opengl_init_params gl = { .get_proc_address = my_get_proc_address };
mpv_render_param params[] = {
    {MPV_RENDER_PARAM_API_TYPE,          (void*)MPV_RENDER_API_TYPE_OPENGL},
    {MPV_RENDER_PARAM_OPENGL_INIT_PARAMS,&gl},
    {MPV_RENDER_PARAM_ADVANCED_CONTROL,  &(int){1}},
    {0},
};
mpv_render_context_create(&rc, mpv, params);
```

The OpenGL backend is special: "It is expected that an OpenGL context is valid and 'current' when calling `mpv_render_*` functions… It must be the same context for the same `mpv_render_context`." — [`render.h`](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html).

The `get_proc_address` callback lets mpv resolve `glFoo` symbols without you having to link against `opengl32.dll` directly. ANGLE-based GL-on-D3D11 is a common way to do this from a D3D11 app.

For each frame:
```c
mpv_opengl_fbo fbo = {.fbo = your_fbo, .w = w, .h = h, .internal_format = 0};
int flip_y = 0;
mpv_render_param rp[] = {
    {MPV_RENDER_PARAM_OPENGL_FBO, &fbo},
    {MPV_RENDER_PARAM_FLIP_Y,     &flip_y},
    {0},
};
mpv_render_context_render(rc, rp);
```

### 2.4 D3D11 / DXGI render API — status

> **Important: there is no D3D11 backend in upstream mpv's render API as of January 2026.**

- Issue [`mpv-player/mpv#5979`](https://github.com/mpv-player/mpv/issues/5979) "d3d11 backend for the Render API" has been open since 2018 and is still open.
- The most prominent attempt, PR [`#12627`](https://github.com/mpv-player/mpv/pull/12627) "add d3d11 backend for the Render API" by `dragonflylee`, was **closed (not merged) in Oct 2023** with maintainer feedback that the render API is "a Frankenstein mess" and should be replaced rather than extended.
- A community gist ([`gist:dragonflylee/244a84cb4e2bff7b25025a7af148c4e2`](https://gist.github.com/dragonflylee/244a84cb4e2bff7b25025a7af148c4e2)) shows the shape of the proposed extension:

  ```c
  typedef struct mpv_dxgi_init_params {
      ID3D11Device       *device;       // host-owned D3D11 device
      IDXGISwapChain     *swapchain;    // host-owned DXGI swapchain
  } mpv_dxgi_init_params;

  // and constants:
  // MPV_RENDER_API_TYPE_DXGI == "dxgi"
  // MPV_RENDER_PARAM_DXGI_INIT_PARAMS
  // Used in mpv_render_param[].
  ```

  These symbols are **not in master** and would only be present if you build libmpv from a fork/patch.
- A follow-up PR, [`#16818`](https://github.com/mpv-player/mpv/pull/16818) "vo_libmpv: introduce 'gpu-next' render backend" (OpenGL-only at the moment, draft status Jan 2026) is intended to be the foundation for a future D3D11/Vulkan render backend.

> Maintainer quote (PR #12627): "the render API/vo_libmpv is basically a Frankenstein mess in my opinion and I honestly would have proposed deprecating it if it wasn't for the fact that macOS uses it as its primary VO… vo_libmpv misses out on all of the improvements we make to vo_gpu_next or what in libplacebo (e.g. anything HDR is going to suck)."

**Practical implication for a Windows wallpaper engine:** until D3D11 lands in the render API, the ways to get mpv pixels into a D3D11 scene are:
1. **Parent-HWND embedding** (`--wid` + `--vo=gpu-next` + `--gpu-api=d3d11`): simplest; mpv draws into its own swapchain on the HWND you give it. No texture handle exposed.
2. **`--gpu-api=d3d11 --d3d11-output-mode=composition`** (PR #16285, merged July 2025): the D3D11 VO exports its swapchain to the host via composition (`DirectComposition`/DComp) instead of owning one. Not yet integrated with libmpv's render API, but exposes the swapchain through `vo_w32_swapchain` internally.
3. **OpenGL render API + ANGLE**: the only upstream-supported path to compose mpv into a D3D11 scene. ANGLE wraps D3D11 as GL ES; mpv renders to a GL FBO (which is a D3D11 texture under the hood) and you can sample that in your D3D11 pipeline with `ID3D11Texture2D::GetSharedResource` / EGL image / D3D11-EGL interop. This is the standard "production" approach for embedders today.
4. **Patch in a D3D11 render API yourself**, or use a third-party build. Practical but carries maintenance burden.

### 2.5 `MPV_RENDER_PARAM_NEXT_FRAME_INFO`

A read-only query on the render context:

```c
mpv_render_frame_info info;
mpv_render_param p = {MPV_RENDER_PARAM_NEXT_FRAME_INFO, &info};
mpv_render_context_get_info(rc, &p);
```

`mpv_render_frame_info` has fields:
- `flags` — bitset of `MPV_RENDER_FRAME_INFO_*`:
  - `PRESENT` (1<<0): a frame is queued.
  - `REDRAW` (1<<1): not a new decoded frame; a re-render of the same frame (e.g. option change while paused).
  - `REPEAT` (1<<2): the renderer should blit the previous frame.
  - `BLOCK_VSYNC` (1<<3): the player expects the user thread to block on vsync — pair with `mpv_render_context_report_swap`.
- `target_time` — when to display this frame (CLOCK_MONOTONIC, seconds).

Source: [`render.h`](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html), [`mpv_render_frame_info` enum](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html).

### 2.6 Frame timing: present-on-signal vs present-every-frame

`mpv_render_context_render()` does not block for vsync by default. The relevant controls:

- `MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME` (default 1) — when rendering, mpv will internally block the call until the queued frame's target time. This is "throttle to video FPS" behavior. Set to 0 to disable.
- `mpv_render_context_report_swap(rc)` — call after you've actually presented the frame (e.g. after `IDXGISwapChain::Present`, after `eglSwapBuffers`, after `SwapBuffers`). mpv uses this to drive A/V sync and pacing. "Note that calling this at least once informs libmpv that you will use this function. If you use it inconsistently, expect bad video playback." — [`render.h`](https://github.com/mpv-player/mpv/blob/master/libmpv/render.h).
- `MPV_RENDER_PARAM_ADVANCED_CONTROL = 1` — promises the host obeys strict threading rules; required for direct rendering (decoded frames go straight into a GPU texture with no copy) and for the SDK to not silently drop update callbacks.

> "If set, the player timing code expects that the user thread blocks on vsync (by either delaying the render call, or by making a call to `mpv_render_context_report_swap()` at vsync time)." — [`render.h`](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html)

For a wallpaper engine the recommended pattern is:
- Drive your own present (D3D11 swapchain on the worker-W / desktop HWND), `SwapChain.Present(1, 0)` to vsync.
- `mpv_render_context_render(rc, rp)` on the render thread to draw the latest frame into your texture.
- `mpv_render_context_report_swap(rc)` immediately after the present.

This is what the community D3D11 gist does (with one critical caveat: it calls Present *after* the render rather than before, which makes the swap ordering a design choice — see §11.4).

### 2.7 Lifecycle of a `mpv_render_context`

```
mpv_create
    -> mpv_initialize (creates core, can start playing audio)
        -> mpv_render_context_create(rc, mpv, params)   // before or after loadfile
            -> [update callback fires; you call mpv_render_context_update]
            -> mpv_render_context_render(rc, ...)       // on render thread
            -> mpv_render_context_report_swap(rc)        // after present
            -> mpv_render_context_free(rc)               // before mpv_terminate_destroy
        -> mpv_terminate_destroy
```

"Calling `mpv_render_context_free()` while a VO is using the render context is active will disable video." "You must free the context with `mpv_render_context_free()` before the mpv core is destroyed. If this doesn't happen, undefined behavior will result." — [`render.h`](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html).

The render context's destruction synchronizes with the VO: it kills the VO asynchronously and processes the dispatch queue until the VO releases. See [`mpv_render_context_free` in `video/out/vo_libmpv.c`](https://github.com/mpv-player/mpv/blob/master/video/out/vo_libmpv.c).

### 2.8 Update callback

```c
void on_update(void *cb_ctx) {
    PostMessage(hwnd, WM_USER_RENDER, 0, 0);
}
mpv_render_context_set_update_callback(rc, on_update, ctx);
```

Rules ([`render.h`](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html)):
- The callback may be called on any internal mpv thread. Do not call any mpv API (other than the ones documented "safe") from inside the callback.
- The OpenGL backend requires the GL context to NOT be current in the callback thread.
- It is a signal that the queue changed; what to do is determined by `mpv_render_context_update(rc)`:

  ```c
  uint64_t flags = mpv_render_context_update(rc);
  if (flags & MPV_RENDER_UPDATE_FRAME) {
      mpv_render_context_render(rc, params);
  }
  ```

The only flag currently defined is `MPV_RENDER_UPDATE_FRAME = 1<<0`.

---

## 3. D3D11 hardware decode path

### 3.1 Codec / GPU matrix (Windows)

mpv's hardware decode backends on Windows are:

| Backend | Vendor | Codecs | Notes |
| --- | --- | --- | --- |
| `d3d11va` | Intel/AMD/NVIDIA | H.264, HEVC, AV1 (varies by GPU), VP9 (limited) | Windows 8+. Default for `--hwdec=auto` when `--vo=gpu + --gpu-context=d3d11/angle`. ([Hardware Decoding docs](https://mpv-player-mpv.mintlify.app/av/hardware-decoding), [discussion #17150](https://github.com/mpv-player/mpv/discussions/17150)) |
| `d3d11va-copy` | Same | Same | Forces video back to system RAM. Avoid. |
| `dxva2` | Any DXVA2-capable | Older; BT.601 forced | "Not safe. Always uses BT.601 for RGB conversion regardless of actual colorspace, causing incorrect colors." Prefer `d3d11va`. ([HW dec docs](https://mpv-player-mpv.mintlify.app/av/hardware-decoding)) |
| `nvdec` | NVIDIA | H.264, HEVC, VP9, AV1 (recent) | "Newest and recommended method for NVIDIA." Use with `--vo=gpu`. |
| `nvdec-copy` | NVIDIA | Same | RAM fallback. |
| `vulkan` | Intel (Broadwell+), AMD (RX 5000/6000+), NVIDIA (recent) | H.264, HEVC, AV1, VP9 (per Vulkan Video spec) | "Requires `--vo=gpu-next`" (per discussion #17150). Windows requires `--gpu-context=winvk`. |
| `vulkan-copy` | Same | Same | RAM fallback. |

For a wallpaper engine, the safe default is `--hwdec=d3d11va --vo=gpu-next --gpu-api=d3d11` on Windows, with the explicit option that the Vulkan path may give different codec coverage depending on driver.

> From [discussion #17150](https://github.com/mpv-player/mpv/discussions/17150), the official support matrix is:
> - `d3d11va` requires `--vo=gpu` with `--gpu-context=d3d11` or `--gpu-context=angle` (Windows 8+).
> - `vulkan` requires `--vo=gpu-next` (any platform with Vulkan Video decoding).

### 3.2 `--hwdec=d3d11va` vs `--hwdec=auto`

`--hwdec=auto` walks the list `d3d11va > nvdec > vulkan` (the order may change per version). It tries them in sequence and falls back to software (`no`) if none work. It also tries to do "direct rendering" (`vd-lavc-dr=yes`) when the VO API is compatible — the decoded frame stays in GPU memory.

`--hwdec=d3d11va` (or any specific name) pins the choice. If it's not available, mpv logs a warning and falls back to software.

> A reported quirk: `hwdec=auto` may not pick the optimal backend. See [discussion #17834](https://github.com/mpv-player/mpv/discussions/17834) where the user observed `vulkan-copy` being chosen over `d3d11va` for 4K HEVC YUV444P10. Workaround: specify `hwdec=d3d11va` explicitly.

### 3.3 `d3d11va` interaction with the VO

- **D3D11VA decode + render:** mpv's D3D11VA decoder uses the `ID3D11Device` from `--gpu-context=d3d11` (or `angle`). Frames stay as `ID3D11Texture2D` references and are passed to `vo_gpu` (the legacy VO) which then uses a SPIR-V → HLSL path. Direct rendering (`vd-lavc-dr=yes`) is supported and avoids a copy.
- **NVDEC decode + D3D11 render:** does not work in current mpv. Per [issue #11151](https://github.com/mpv-player/mpv/issues/11151), "`--gpu-api=d3d11 -vo=gpu-next` does not support `--hwdec=nvdec`". You'd have to use `--gpu-api=vulkan` (with `nvdec` then working) or `--gpu-api=opengl` (which uses CUDA → OpenGL interop).
- **Vulkan hwdec + gpu-next:** the only way to combine `vulkan` hwdec with the modern VO. Requires the Vulkan Video extensions (drivers ≥ NVIDIA 580.76, AMD 22.11.2+ on RX 5000/6000, Intel on Broadwell+).

For a wallpaper engine that wants H.264 + HEVC on any vendor:
```text
hwdec=d3d11va          # or "auto" with explicit fallback
vo=gpu-next            # modern VO
gpu-api=d3d11          # render to D3D11
gpu-context=d3d11      # win32 + D3D11
# optional: vd-lavc-dr=yes (default if hw & vo support it)
```

### 3.4 When hwdec is not available

mpv falls back to software decode. Software HEVC 4K is ~80–90 % CPU. For a wallpaper engine, expect this on virtual machines, on machines with broken drivers, or when the user is in a Remote Desktop session. Have a CPU budget.

Codec coverage that the OS *can't* hardware-decode (even with `d3d11va`): typically some 4:4:4 profiles, 12-bit HEVC on older Intel, and AV1 on pre-Haswell Intel. mpv logs a warning and decodes in software. ([discussion #17834](https://github.com/mpv-player/mpv/discussions/17834))

---

## 4. Resource lifecycle

### 4.1 Creation order

1. `setlocale(LC_NUMERIC, "C")` — required before `mpv_create`. ([client.h:185–189](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h))
2. `mpv_create` → returns main `mpv_handle`. Uninitialized state.
3. Set options that are only read at init time:
   - `config`, `config-dir`, `input-conf`, `load-scripts`, `script`, `player-operation-mode`, `input-app-events` (macOS), and **all encoding-mode options**. ([client.h `mpv_initialize` docs](https://mpv.dpldocs.info/~master/mpv.client.mpv_initialize.html))
   - For libmpv, defaults are already set via the `libmpv` profile (no terminal, no config, idle, etc.).
4. `mpv_initialize(mpv)`.
5. Set runtime options (`vo`, `hwdec`, `gpu-api`, `gpu-context`, `loop`, `mute`, `no-audio`, `no-input`, `no-osc`, …).
6. *(Optionally)* `mpv_render_context_create(&rc, mpv, params)` — must happen before playback actually causes a VO to be created. Recommended immediately after init.
7. `mpv_observe_property(...)`, `mpv_request_event(MPV_EVENT_END_FILE, 1)`, etc.
8. `mpv_command(mpv, (const char*[]){"loadfile", path, NULL})`.

### 4.2 Destruction order (the safe one)

The render API is strict about this ([`render.h`](https://github.com/mpv-player/mpv/blob/master/libmpv/render.h)):

> "Calling `mpv_render_context_free()` while a VO is using the render context is active will disable video. You must free the context with `mpv_render_context_free()` before the mpv core is destroyed. If this doesn't happen, undefined behavior will result."

And the core enforces it: in [`player/client.c::mp_clients_destroy`](https://github.com/mpv-player/mpv/blob/master/player/client.c):

```c
if (mpctx->clients->render_context) {
    MP_FATAL(mpctx, "Broken API use: mpv_render_context_free() not called.\n");
    abort();
}
```

So the safe order is:

1. Stop issuing new commands (e.g. `mpv_command("quit")` or just stop driving `mpv_wait_event`).
2. `mpv_render_context_free(rc)` — disables video, joins the VO thread, frees all GPU objects mpv created.
3. `mpv_unobserve_property` for everything you observed (cosmetic; helps avoid late events).
4. `mpv_terminate_destroy(mpv)` — sends `quit`, waits for the core to die, frees the handle. Blocking.
5. Release any D3D11 resources you allocated for the device/swapchain that were used by the (now-freed) render context.

If you have multiple `mpv_create_client` handles, free the render context *once* (per core), then `mpv_destroy` each handle, then `mpv_terminate_destroy` on the last.

### 4.3 When must `mpv_wait_event` / `mpv_render_context_render` be called?

- **`mpv_wait_event(timeout)`** — can be polled on any thread, but only one thread at a time per handle. The proper way to drive mpv without a dedicated thread: poll it from your message loop on a non-blocking timeout (e.g. 0 to 50 ms). Setting the wakeup callback and using a Windows `PostMessage(hwnd, WM_USER, 0, 0)` pattern is more efficient. ([`client.h`](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h))
- **`mpv_render_context_render`** — on the render thread (which owns the GL context / D3D11 device). Never from inside the update callback or wakeup callback.

### 4.4 `mpv_render_context_set_update_callback`

Already covered in §2.8. The contract: a signal, not a render command. You `Set update callback → wake up your render thread → call mpv_render_context_update() → if `MPV_RENDER_UPDATE_FRAME` is set, call `mpv_render_context_render()`.

### 4.5 `mpv_render_context_report_swap`

Already covered in §2.6. Call after `SwapChain.Present` / `eglSwapBuffers` / `SwapBuffers`. The function increments an internal flip counter that mpv uses for A/V sync.

> "calling this at least once informs libmpv that you will use this function. If you use it inconsistently, expect bad video playback." — [`render.h`](https://github.com/mpv-player/mpv/blob/master/libmpv/render.h)

Source: [`video/out/vo_libmpv.c::mpv_render_context_report_swap`](https://github.com/mpv-player/mpv/blob/master/video/out/vo_libmpv.c).

---

## 5. Threading model

### 5.1 What mpv owns internally

After `mpv_initialize`, mpv has:
- A **core thread** (`mp_thread_create(core_thread, …)` from `mpv_create`). This is where the demuxer, scheduler, and main control flow live. ([`player/client.c::mpv_create`](https://github.com/mpv-player/mpv/blob/master/player/client.c))
- A **VO thread**, created by whichever VO is in use (for `--vo=gpu`/`gpu-next`, this is the renderer's display loop).
- A **thread pool** for async jobs (FFmpeg decoding uses it): `mp_thread_pool_create(0, 1, 30)` — initial count 0, max 30. ([`player/main.c::mp_create`](https://github.com/mpv-player/mpv/blob/master/player/main.c))
- The libavcodec decoder may spawn its own threads per the codec's `thread_count` (controlled by `--vd-lavc-threads`).

### 5.2 What the host owns

You should have:

| Thread | Responsibility | Forbidden |
| --- | --- | --- |
| **Control / event thread** | Drive `mpv_wait_event(timeout)`. Dispatch `mpv_set_property` / `mpv_command` from here. Read observed-property events. | Do not call `mpv_render_context_render` from here (use the render thread). |
| **Render thread** | Owns the GL context or D3D11 device. Loops: wait on `update_callback` signal → call `mpv_render_context_update` → if frame, call `mpv_render_context_render` → present (your own code) → `mpv_render_context_report_swap`. | Do not call any other client API except those marked "safe". Do not call mpv_render_* from inside the update callback. |
| (Optional) **Custom message thread** | If you want a UI to send commands, marshal them to the control thread (e.g. via `PostMessage`). mpv serializes via the `mp_client_api.lock`, so direct calls are thread-safe but block the core. |

### 5.3 What the update callback must do

It must wake up the render thread, not perform work itself. Typical patterns:

- Win32: `PostMessage(hwnd, WM_USER_RENDER, 0, 0)` and let the WndProc's `WM_USER_RENDER` handler drive `mpv_render_context_update` + `mpv_render_context_render`. ([community d3d11 gist](https://gist.github.com/dragonflylee/244a84cb4e2bff7b25025a7af148c4e2))
- SDL2: `SDL_PushEvent(&render_event)`.
- Headless: an `std::condition_variable::notify_one()`.

### 5.4 Is there a "main loop" the host must run?

No. There is no `mpv_run()`-style function. mpv is purely event/command driven. You can:

- Drive `mpv_wait_event` from your existing `GetMessage` / `DispatchMessage` loop (best for Windows apps).
- Drive it on a dedicated thread (e.g. if your host is a service / CLI / wallpaper worker that doesn't have a GUI thread).
- Drive it on a timer (e.g. a `WM_TIMER` at 30 Hz) and ignore the wakeup callback. The downside is coarser event latency and less responsive `mpv_observe_property` notifications.

The recommended pattern is: `mpv_set_wakeup_callback` + your message pump. When mpv calls back, `PostMessage` the host window; in the message handler, drain events with `mpv_wait_event(0)` in a loop until you get `MPV_EVENT_NONE`.

### 5.5 Per the `render.h` "Threading" section

> "Preferably rendering should be done in a separate thread. If you call normal libmpv API functions on the renderer thread, deadlocks can result (these are made non-fatal with timeouts, but user experience will obviously suffer)."

Specific rules ([`render.h`](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html)):
1. Only one `mpv_render_*` call at a time per core.
2. Never call `mpv_render_*` from inside a wakeup callback or update callback.
3. If the OpenGL backend is used, the GL context must be current in the calling thread and must be the same context the `mpv_render_context` was created with.
4. The render thread must not call libmpv API other than `mpv_render_*` plus the "safe" functions.
5. If you set `MPV_RENDER_PARAM_ADVANCED_CONTROL=1`, you promise (3) and (4) hold. A real deadlock (not a timeout-rescued one) is possible if you break the promise.

---

## 6. Event and property model

### 6.1 `mpv_observe_property` vs `mpv_set_property`

| | `mpv_set_property` | `mpv_observe_property` |
| --- | --- | --- |
| Effect | Writes a value now. | Subscribes to changes. |
| Returns | error code | error code |
| Back-channel | none | `MPV_EVENT_PROPERTY_CHANGE` with `reply_userdata` you registered |
| Format | `MPV_FORMAT_*` | `MPV_FORMAT_*` (including `MPV_FORMAT_NONE` for low-level "may have changed" notifications) |
| Coalescing | n/a | yes — "change events are returned only once the event queue becomes empty" ([client.h](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h)) |

Coalescing means a property that flips 10 times in a single render tick arrives as **one** `MPV_EVENT_PROPERTY_CHANGE` with the latest value. This is what you want for `pause`, `mute`, `time-pos`, `playlist-pos`, `eof-reached`, etc.

### 6.2 `mpv_event` and `mpv_event_id`

```c
typedef struct mpv_event {
    mpv_event_id event_id;
    int          error;            // reply status (async reqs)
    uint64_t     reply_userdata;   // matches your async req, or 0
    void        *data;             // event-specific
} mpv_event;
```

Source: [`client.h` `mpv_event`](https://www.ccoderun.ca/programming/doxygen/mpv/structmpv__event.html), [`client.h` enum](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h).

| Event id | When | `data` type | Useful for |
| --- | --- | --- | --- |
| `MPV_EVENT_NONE = 0` | timeout or spurious wakeup | NULL | end of drain |
| `MPV_EVENT_SHUTDOWN = 1` | core is shutting down | NULL | exit the message loop |
| `MPV_EVENT_LOG_MESSAGE = 2` | log line at your requested level | `mpv_event_log_message*` | debug, integration logging |
| `MPV_EVENT_GET_PROPERTY_REPLY = 3` | reply to `mpv_get_property_async` | `mpv_event_property*` | rare; prefer observe |
| `MPV_EVENT_SET_PROPERTY_REPLY = 4` | reply to `mpv_set_property_async` | NULL | rare; prefer sync set |
| `MPV_EVENT_COMMAND_REPLY = 5` | reply to `mpv_command_async` | `mpv_event_command*` | async commands |
| `MPV_EVENT_START_FILE = 6` | about to load a file | `mpv_event_start_file*` | debug |
| `MPV_EVENT_END_FILE = 7` | playback of a file ended | `mpv_event_end_file*` (`.reason`) | loop logic, error reporting |
| `MPV_EVENT_FILE_LOADED = 8` | file headers read, decode starts | NULL | debug |
| `MPV_EVENT_VIDEO_RECONFIG = 18` | video format changed (size, fps) | NULL | re-allocate render targets |
| `MPV_EVENT_AUDIO_RECONFIG = 19` | audio format changed | NULL | rare |
| `MPV_EVENT_SEEK = 20` | seek started | NULL | debug |
| `MPV_EVENT_PLAYBACK_RESTART = 21` | playback resumed after seek | NULL | debug |
| `MPV_EVENT_PROPERTY_CHANGE = 22` | observed property changed | `mpv_event_property*` | primary control channel |
| `MPV_EVENT_QUEUE_OVERFLOW = 24` | events dropped | NULL | log/panic |
| `MPV_EVENT_HOOK = 25` | registered hook fired | `mpv_event_hook*` | advanced scripting |

`mpv_event_end_file::reason`:
- `MPV_END_FILE_REASON_EOF = 0`
- `MPV_END_FILE_REASON_STOP = 1`
- `MPV_END_FILE_REASON_QUIT = 2`
- `MPV_END_FILE_REASON_ERROR = 3`
- `MPV_END_FILE_REASON_REDIRECT = 4`

For a wallpaper engine, the minimum event set is `END_FILE`, `PROPERTY_CHANGE` (for `pause`, `mute`, `time-pos`), and `SHUTDOWN`.

### 6.3 `mpv_command_async` vs `mpv_command`

`mpv_command` blocks until the command is processed. Reasonable for `loadfile`, `playlist-play-index`, `set` (where the value is a literal string parsed by mpv). For long-running commands (e.g. `screenshot`), use `mpv_command_async(ctx, userdata, args)`. The reply comes back as `MPV_EVENT_COMMAND_REPLY` with the userdata you passed.

Source: [`mpv_command_async`](https://mpv.dpldocs.info/~master/mpv.client.mpv_command_async.html).

---

## 7. Memory management

### 7.1 What you must free

- **Any string returned via `MPV_FORMAT_STRING` or `MPV_FORMAT_OSD_STRING`** (e.g. `mpv_get_property_string`, `mpv_get_property_osd_string`, `mpv_get_property` with a `char*` out param) → `mpv_free(result)`. ([`client.h` `mpv_format` enum](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h))
- **`mpv_node` returned via `MPV_FORMAT_NODE`** → `mpv_free_node_contents(&node)`. Does not free the `mpv_node` struct itself, only the data it points to. ([`client.h`](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h))
- **Strings in `mpv_event_log_message`** (`text`, `prefix`, `level`): mpv owns these. They are valid only until the next `mpv_wait_event` call returns a different event. If you need to retain them, copy. ([`mpv_event_log_message` docs](https://www.ccoderun.ca/programming/doxygen/mpv/structmpv__event__log__message.html))
- **Strings in `mpv_event_property` / `mpv_event_client_message` / `mpv_event_end_file`**: mpv owns them; valid until the next `mpv_wait_event` returns a different event. Copy if needed.

> From `mpv_free` docs: "General function to deallocate memory returned by some of the API functions. Call this only if it's explicitly documented as allowed. Calling this on mpv memory not owned by the caller will lead to undefined behavior." ([`client.h`](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h))

### 7.2 What you do NOT free

- The `mpv_event` struct itself (mpv reuses it from a ring buffer; pointer remains valid until next `mpv_wait_event`).
- `mpv_handle*` and `mpv_render_context*` — use `mpv_destroy` / `mpv_render_context_free`.
- Strings you passed *to* `mpv_set_property` / `mpv_set_option_string` / `mpv_command` (you own these).
- D3D11 resources you created; you own them.

### 7.3 Allocation: no global allocators you can hook

mpv uses `talloc` internally for the playback core (this is not exposed to the host). All host-visible memory comes from `malloc`/`free` semantics on the C side. There is no `mpv_set_allocator` function. The only memory-related call is `mpv_free(void*)` and `mpv_free_node_contents(mpv_node*)`.

---

## 8. Configuration

### 8.1 Options to set BEFORE `mpv_initialize`

From [`mpv_initialize` docs in `client.h`](https://mpv.dpldocs.info/~master/mpv.client.mpv_initialize.html):

- `config` — whether to read `mpv.conf`
- `config-dir` — where to find config
- `input-conf`, `load-scripts`, `script`
- `player-operation-mode` — controls "pseudo-GUI" auto-enable
- `input-app-events` (macOS only)
- All **encoding mode** options

For a wallpaper engine, you almost certainly want to set `config=no` (default for libmpv) so user-installed mpv config doesn't surprise you.

### 8.2 Options to set after init

Everything else. Most of the per-file options can also be changed at runtime via `mpv_set_property` (e.g. `pause`, `mute`, `loop-file`, `hwdec`).

### 8.3 Recommended wallpaper-engine config

Compiled from [Hardware Decoding docs](https://mpv-player-mpv.mintlify.app/av/hardware-decoding), [Audio Output docs](https://mpv-player-mpv.mintlify.app/av/audio-output), [the `wid` wallpaper recipe](https://github.com/mpv-player/mpv/issues/7790), and the community D3D11 gist.

| Option | Value | Why |
| --- | --- | --- |
| `wid` | `(int64_t)hwnd` | embed in host window |
| `vo` | `gpu-next` | modern renderer, libplacebo HDR |
| `gpu-api` | `d3d11` | match host's D3D11 |
| `gpu-context` | `d3d11` | win32 + D3D11 swapchain |
| `hwdec` | `d3d11va` (or `auto`) | H.264/HEVC on all vendors |
| `loop-file` | `inf` | wallpapers are loops |
| `loop-playlist` | `inf` | if you use a playlist |
| `mute` | `yes` | default silence |
| `ao` | `null` (or `wasapi` if you want audio) | avoid audio device init issues |
| `no-audio` | set if you want to disable the audio decoder entirely (more aggressive than `ao=null`) | save CPU on silent videos |
| `no-input` | `yes` | no input, no key bindings fire |
| `no-input-default-bindings` | `yes` | belt-and-braces |
| `no-osc` | `yes` | no on-screen controller |
| `no-osd-bar` | `yes` | no OSD |
| `cursor-autohide` | `no` | never grab cursor |
| `input-cursor` | `no` | never change cursor |
| `keep-open` | `no` | do not pause on EOF (we loop) |
| `pause` | `no` | play immediately |
| `idle` | `yes` (default for libmpv) | when playlist ends, stay alive |
| `force-window` | `yes` (if no file) | idle mode still has a window |
| `reset-on-next-file` | `no` | keep our config across files |
| `terminal` | `no` (default for libmpv) | don't touch stdio |
| `msg-level` | `all=v` or higher | tune for logs |
| `video-timing-offset` | `0` | remove libmpv's internal sleep; you drive present |
| `vd-lavc-dr` | `yes` (default if hw+vo allow) | direct-render, no copy |
| `d3d11-output-mode` | `composition` (merged July 2025) | export swapchain (newer mpv only) |
| `d3d11-flip` | `yes` (default) | DXGI flip-model swapchain |
| `d3d11-sync-interval` | `1` (default) | match your present interval |
| `d3d11-adapter` | `<GPU name>` (optional) | force a specific GPU |

If you want the wallpapers behind desktop icons (true wallpaper), you need either:
- A worker window hosted by a shell like `progman` with `0x00000040` (`WS_EX_TOOLWINDOW`) and the `SendMessage(0x0312, 0, ...)` "send-to-bottom" trick — but mpv's `wid` flow doesn't expose the `HWND_BOTTOM` SendMessage; the host has to do that.
- A `--wid=0` setup where mpv creates its own root window; you then `SetParent` it into a `WorkerW` slot. mpv does not provide a `SetParent` API; you do it from the host after the window exists (track via `MPV_EVENT_VIDEO_RECONFIG` or by enumerating child HWNDs).

`--reset-on-next-file=no` is **not** a wallpaper thing — it's a per-file-options modifier. The doc claims: "Resets the value of some options on the next file. […] If you don't want options to be reset when the next file is loaded, you can set the option `reset-on-next-file=no`." ([`options.rst`](https://github.com/mpv-player/mpv/blob/master/DOCS/man/options.rst)) For a wallpaper that swaps files via `loadfile`, you likely *want* resets so each file starts fresh.

### 8.4 `--keep-open` vs `--loop`

From [`options.rst` `--loop-file`](https://github.com/mpv-player/mpv/blob/master/DOCS/man/options.rst) and [issue #9470](https://github.com/mpv-player/mpv/issues/9470):

- `--loop-file=inf` (a.k.a. `--loop=inf`): seek to 0 at EOF. "Counts the number of times it causes the player to seek to the beginning of the file, not the number of full playthroughs. This means `--loop-file=1` will end up playing the file twice."
- `--loop-playlist=inf`: replay the whole playlist. Default behavior you'd want for a list of wallpapers.
- `--keep-open=always`: pause at EOF; never exit idle mode. **Conflicts with `--loop-file=inf` because the only way to break out of `keep-open=always` is an explicit seek or playlist change, and `loop-file` *is* a seek.** Issue #9470 confirms this is by design. Don't combine them for a wallpaper; just use `--loop-file=inf` and the `MPV_EVENT_END_FILE` will fire `MPV_END_FILE_REASON_EOF` each time the loop seeks, but playback will continue.

---

## 9. Comparison anchor to VLC/LibVLC (key contrasts only)

### 9.1 Embedding model

- **VLC (`libvlc_media_player_set_hwnd`):** you give VLC a HWND. VLC creates its own Direct3D (or OpenGL) device and swapchain on that HWND. Compositing is by Windows z-order. Equivalent to mpv's `wid` flow. VLC has no render-API concept equivalent to libmpv's `mpv_render_context` for host-owned swapchains.
- **VLC `IMemory` / DirectComposition path (`vlc_video_set_output_callbacks` or the more modern `libvlc_video_set_d3d11_output` / DComp integration in VLC 3.0+):** you can get decoded frames via callback (CPU) or via DComp visual hosting (GPU). This is closer to what an `mpv_render_context` would do *if* mpv had a D3D11 render API, but is more mature and more battle-tested on Windows.
- **mpv:** `wid` for HWND embedding, `mpv_render_context` for GL composition. **No upstream D3D11 render backend** as of Jan 2026.

### 9.2 HW decode

Both expose D3D11VA / NVDEC. VLC's naming: `--avcodec-hw=d3d11va` (vs mpv's `--hwdec=d3d11va`). In practice, both end up calling into the same D3D11 video APIs. VLC's `dxva2` is also still exposed and is similarly problematic to mpv's `dxva2` (BT.601 forced).

### 9.3 Render surface ownership

- **VLC:** defaults to owning the window + swapchain. You can request raw callbacks (`--vmem`, `--vmem-emu`) or DirectComposition (a build-time feature in VLC 3.x) for texture-level access.
- **mpv:** GL composition via `mpv_render_context` is the standard, well-supported path. D3D11 composition via render API: not upstream. D3D11 swapchain export via `--d3d11-output-mode=composition`: merged July 2025 but not yet exposed to libmpv.

### 9.4 Audio

- **VLC:** `--no-audio` (a global option, disables audio output subsystem).
- **mpv:** `--no-audio` or `--aid=no` (track selection, leaves audio decoder alive by default); `--ao=null` (decodes audio, throws it away, but maintains video timing) or just don't set audio (`aid=no` + no AO). For a wallpaper, the cleanest is `ao=null` to keep video timing driven by audio clock. ([`ao.rst`](https://github.com/mpv-player/mpv/blob/master/DOCS/man/ao.rst))

### 9.5 Config complexity

- **VLC** is famously option-heavy; thousands of module options.
- **mpv** is more opinionated and has a smaller, curated option set. Per the docs: "The libmpv C API is documented directly in this header. Note that most actual interaction with this player is done through options/commands/properties, which can be accessed through this API." ([`client.h`](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h))

For a wallpaper engine, mpv is usually less config to deal with; the trade-off is that some advanced composition options VLC exposes don't have a direct mpv analog.

---

## 10. Distribution & licensing

### 10.1 License

- **mpv core:** GPLv2+ by default. The libmpv API itself (the headers and ABI) is ISC-licensed, which means a clean header-only distribution is fine. ([libmpv API page](https://mpv-player-mpv.mintlify.app/embedding/libmpv))
- **LGPL build:** `mpv` can be built with `-Dgpl=false` (Meson option), which disables GPL components and produces an LGPLv2.1+ build. The community builds of this for Windows are published by `mpv-winbuild` projects.

### 10.2 Prebuilt Windows binaries

The [`zhongfly/mpv-winbuild`](https://github.com/zhongfly/mpv-winbuild), [`erickyun/mpv-winbuild`](https://github.com/erickyun/mpv-winbuild), and [`mitzsch/mpv-winbuild`](https://github.com/mitzsch/mpv-winbuild) projects ship auto-built archives on every mpv commit:

- `mpv-x86_64-YYYYMMDD-git-XXXXXXX.7z` — full mpv.exe + console.
- `mpv-dev-x86_64-YYYYMMDD-git-XXXXXXX.7z` — **libmpv-2.dll** + headers + import lib.
- `mpv-dev-lgpl-x86_64-…7z` — **LGPLv2.1+ build**, with LGPLv3 ffmpeg statically linked.

From the [zhongfly README](https://github.com/zhongfly/mpv-winbuild):

> "mpv-dev-xxxx.7z is libmpv, including the libmpv-2.dll file. Some media players based on libmpv use libmpv-2.dll or mpv-2.dll. You can upgrade their libmpv by overwriting this dll. mpv-dev-lgpl-xxxx.7z is libmpv under LGPLv2.1+ license, which disables LGPLv2.1+ incompatible packages and statically links to ffmpeg under LGPLv3."

From the release page snapshot ([2026-04-12 release](https://github.com/zhongfly/mpv-winbuild/releases/tag/2026-04-12-062f4bf)) the 7z archives are roughly 25–31 MB compressed. The unpacked `libmpv-2.dll` is in the 25–30 MB range because of ffmpeg + Lua + shaderc + spirv-cross + etc. Concretely:

| Archive | Compressed size |
| --- | --- |
| `mpv-x86_64-20260412-….7z` | 30,336 KB |
| `mpv-dev-x86_64-20260412-….7z` | 29,599 KB |
| `mpv-dev-lgpl-x86_64-20260412-….7z` | 25,713 KB |
| `ffmpeg-x86_64-20260412-….7z` (shared) | 25,850 KB |

The `mpv-dev-lgpl` build is slightly smaller because it drops GPL-only encoders/filters.

### 10.3 DLL size and dependencies

After unpacking `mpv-dev-*.7z` you get (approximate, build-dependent):

```
libmpv-2.dll          ~50–70 MB (statically links most everything)
libmpv-2.lib          import library for MSVC
include/mpv/          C headers
```

Runtime dependencies (Windows):
- `msvcp140.dll`, `vcruntime140.dll`, `vcruntime140_1.dll` — MSVC 2015–2022 redistributable. (mpv is built with MSVC by the `mpv-winbuild` pipeline since 2022; older builds used MinGW.)
- `d3d11.dll`, `dxgi.dll` — Windows 10+ has these.
- `d3dcompiler_47.dll` — for HLSL → DXBC at runtime if mpv uses dynamic shader compilation. Sometimes needed; sometimes system-provided.
- `WinMM.dll` (`timeBeginPeriod`) — system.
- Optional: `libusb-1.0.dll` if you enable certain input backends.

For a wallpaper engine distributed as a single installer, expect to either ship the VC++ redist or use static linking. The `mpv-dev-lgpl` build is the one to ship if you have any concern about GPL contamination of your own app — but note that LGPLv2.1+ still has linkage/source-distribution obligations.

### 10.4 NuGet

There is no "official" Microsoft-published NuGet package for libmpv. Community packages exist:
- `LibMpv.Client` (and older `Mpv.NET`, `mpv.NET`, `libmpv-net`) — various wrappers, some in C#, some in C++/CLI.
- For a direct C/C++ project, the cleanest approach is to unpack `mpv-dev-lgpl-*.7z` into a `third_party/libmpv/` directory in your repo and add the include path + import lib to your project.

### 10.5 License choices for a closed wallpaper app

- **GPL core + dynamic linking:** legal for closed source because libmpv is a system library (LGPL-style argument) — but this is not the LGPL argument, it's the "mere aggregation" one. You should still comply with mpv's GPL (offer source for the libmpv you distribute, attribution, no additional restrictions).
- **LGPL build (`mpv-dev-lgpl`):** use this. Your app can be closed source as long as you allow users to relink a different `libmpv-2.dll` (i.e. don't statically link the LGPL build into your exe).

For a Windows wallpaper engine shipped as a single `.exe` (or as an MSIX / Click-Once), you almost certainly need the LGPL build. Consult your lawyer.

---

## 11. Known issues / footguns for embedders

### 11.1 `--wid` + D3D11 swapchain ownership

When you pass `wid`, mpv creates a child HWND inside yours and creates a DXGI swapchain on it. Implications:

- The child HWND's resize is driven by mpv, not you. If you resize the parent, mpv's swapchain may be invalidated and need a `wm_size` round-trip. You usually handle this by sending `WM_SIZE` to the child (or by tracking the actual child HWND and forwarding).
- Some D3D11 fullscreen transition code paths assume mpv owns the top-level window. `--wid` mostly defangs this, but in exclusive fullscreen (`--fs`) and on multi-monitor setups there are corner cases. ([mpv FAQ](https://github.com/mpv-player/mpv/wiki/FAQ): "Windows uses the native d3d11 backend by default now. If you experience problems, try playing with any of the --d3d11-... options.")
- DComp / `composition` output mode (PR #16285, merged July 2025) was added specifically to address the swapchain-ownership mess, but it is a VO-internal feature and is not yet wired to the libmpv render API.

### 11.2 `gpu-next` vs `gpu`

`vo=gpu` is the legacy renderer (RA → custom shader pipelines). `vo=gpu-next` is the new libplacebo-based renderer (better colors, HDR, scaling). For a 2026 wallpaper engine, **use `gpu-next`**, with these caveats:

- `vulkan` hwdec requires `gpu-next` exclusively.
- `gpu-next` is the future and most new features land here first. ([FAQ](https://github.com/mpv-player/mpv/wiki/FAQ): "--vo=gpu is essentially the default … but `--profile=high-quality` is for selecting a preset with advanced scaling and so on (replaces `--profile=gpu-hq`)".)
- mpv's `gpu-hq` profile is officially marked deprecated in source. Use the `high-quality` profile if you want a preset.

### 11.3 Audio device initialization on Windows

Default audio output is `wasapi` (shared mode). It will:
- Open the default audio endpoint.
- Start a stream, even if you set `mute=yes`.
- Occasionally fail to find a device (Remote Desktop, audio service disabled). This kills audio init and you'll see `[ao/wasapi] Init failed` but the rest of mpv keeps going.

For a wallpaper engine:
- Use `ao=null` to make audio entirely free of OS dependencies. Video timing is still driven by mpv's internal audio clock emulation, which is fine.
- If you want audio, `ao=wasapi` is the right choice. Don't set `audio-exclusive=yes`; it's lower latency but causes failures on consumer endpoints.

`--no-audio` (the property) is not the same as `ao=null`. `--no-audio`/`--aid=no` disables the audio decoder; `ao=null` keeps the decoder and just discards output. For pure-video wallpapers, `aid=no` saves more CPU; for wallpapers with optional audio, `ao=null` is safer.

### 11.4 mpv stealing focus from the parent window

This is a known complaint ([mpv issue #7790](https://github.com/mpv-player/mpv/issues/7790), [#7725](https://github.com/mpv-player/mpv/issues/7725)). When you create the child HWND with `wid`, mpv may call `SetFocus` or `SetForegroundWindow` on its child. Effects:

- The taskbar flashes.
- Start-menu search becomes inconsistent.
- The user perceives "the wallpaper came to the front" and clicks the desktop through it.

Workarounds:
- Create the parent HWND with `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE`.
- Don't make the child window focusable: use `WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS` and `WS_EX_NOPARENTNOTIFY`.
- Pass `no-input-default-bindings` + a Lua script that returns early from the input handler.
- For LibVLC, this is similarly tricky; both have the same root cause (Windows focus rules).

For a true "behind icons" wallpaper you also need the `progman` / `WorkerW` trick to host a HWND behind the desktop icons. This is a Windows shell hack, not an mpv issue.

### 11.5 `--keep-open` vs `--loop`

Already covered in §8.4. The summary: do not combine `keep-open=always` with `loop-file=inf`. Pick one. For wallpapers, `loop-file=inf` is the right choice.

### 11.6 `--no-input` and pointer events

`--no-input` does not pass-through pointer events to the window behind mpv's. mpv consumes them at the message-loop level. To forward events to a window behind mpv:

- Make mpv's child HWND `WS_EX_TRANSPARENT` (click-through) — but this also disables mpv's ability to see clicks for itself, so you lose nothing if you didn't want input anyway.
- Use `WS_EX_LAYERED` with a colorkey for partial click-through. Doesn't help with mouse wheel or keyboard.
- If you want to mix mpv with interactive UI, the render-API path is the only real solution: render mpv to a FBO, composite in your own UI layer, and never let mpv own a window.

`--input-cursor=no` and `--cursor-autohide=no` only affect cursor *display*; they don't affect whether mpv receives input.

### 11.7 D3D11 render API not in upstream

The single biggest footgun. The closed PR #12627 / gist shows what the extension *would* look like, but you cannot depend on it being in `mpv-2.dll` from mpv-winbuild. Verify by:
```bash
strings libmpv-2.dll | grep -E "MP4D|MPV_RENDER_API_TYPE_DXGI|mpv_dxgi_init"
```
If those strings are absent, you have the upstream build and must use the `wid` + `vo=gpu-next` + `gpu-api=d3d11` path or the OpenGL-via-ANGLE render API.

### 11.8 LC_NUMERIC, signal handlers, FPU

From [`client.h` "Basic environment requirements"](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h):

- **`LC_NUMERIC` must be `"C"`.** If your app calls `setlocale(LC_ALL, "")` (which most GUI apps do at startup), you must follow it with `setlocale(LC_NUMERIC, "C")` before `mpv_create`. The C runtime check is in [`player/main.c::check_locale`](https://github.com/mpv-player/mpv/blob/master/player/main.c).
- **FPU precision** must be at least double. MSVC defaults to 24-bit (single) — call `_controlfp(_PC_64, _MCW_PC)` if you haven't already. (Most modern apps don't change this from default.)
- **On Windows, mpv calls `timeBeginPeriod(1)`** at init. This affects the system timer resolution globally. If your app is sensitive to power, you may want to call `timeEndPeriod(1)` after `mpv_terminate_destroy` — but mpv does not export a way to undo this. Expect 1 ms OS timer granularity for the rest of the process.
- **Don't override `SIGCHLD`** or call `wait()` for all PIDs. mpv manages its own child processes (for `--ytdl`, screenshot encoding, etc.).
- **Signal handlers** should use `SA_RESTART`. mpv may run signal handlers internally; this is mostly an issue on POSIX but worth knowing.
- mpv uses libraries that are not "library-safe" with respect to global state: Fribidi (via libass), ALSA (Linux), FFmpeg. On Windows this is mostly a non-issue but FFmpeg's own global state can show up if you mix mpv with other FFmpeg consumers in the same process.

### 11.9 `mpv_set_property` write semantics

`mpv_set_property` is **write-only and best-effort**. A property you write may be overridden by the playback core on the next event. Use `mpv_observe_property` to read back the canonical value.

### 11.10 `vid` and per-track selection

For multi-angle or multi-video-track media (Blu-ray, some MP4), track selection can reset on file change (per [`options.rst --aid` notes](https://github.com/mpv-player/mpv/blob/master/DOCS/man/options.rst)). For a wallpaper engine that re-loads a list, this can produce surprising "audio track changed" or "video track changed" events.

### 11.11 Render context + `gpu-context=angle`

If you use the OpenGL render API and ANGLE under the hood (`gpu-context=angle`), make sure your D3D11 device and ANGLE's D3D11 device can share resources. With ANGLE's `EGL_D3D11_DEVICE` you can pass an existing D3D11 device to ANGLE; without it, ANGLE creates its own device and resource interop is much harder. This is undocumented by mpv but documented in ANGLE's docs.

### 11.12 libmpv 32-bit builds

`mpv-winbuild` (zhongfly/erickyun) publishes 64-bit only. For 32-bit, you must build yourself or find an archive. Few wallpaper engines need 32-bit in 2026; flag this if you do.

---

## 12. Appendix — key code references

- Client API spec: [`include/mpv/client.h`](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h)
- Render API spec: [`libmpv/render.h`](https://www.ccoderun.ca/programming/doxygen/mpv/render_8h.html) (doxygen mirror, with the master source at `libmpv/render.h` in the repo)
- OpenGL render backend: [`video/out/libmpv_gpu.c`](https://github.com/mpv-player/mpv/blob/master/video/out/gpu/libmpv_gpu.c)
- Render context impl: [`video/out/vo_libmpv.c`](https://github.com/mpv-player/mpv/blob/master/video/out/vo_libmpv.c)
- VO (gpu) threading / event flow: [`video/out/vo_libmpv.c`](https://github.com/mpv-player/mpv/blob/master/video/out/vo_libmpv.c) (the `flip_page` function is the canonical example of how the VO waits for the host to call `mpv_render_context_render` and `mpv_render_context_report_swap`)
- D3D11 VO context: [`video/out/d3d11/context.c`](https://github.com/mpv-player/mpv/blob/master/video/out/d3d11/context.c)
- D3D11 RA: [`video/out/d3d11/ra_d3d11.c`](https://github.com/mpv-player/mpv/blob/master/video/out/d3d11/ra_d3d11.c)
- Client API impl (locking, refcounts, mpv_create internals): [`player/client.c`](https://github.com/mpv-player/mpv/blob/master/player/client.c)
- Main loop / locale check: [`player/main.c::check_locale`](https://github.com/mpv-player/mpv/blob/master/player/main.c)
- Manual: <https://mpv.io/manual/master/>
- Embedding examples: <https://github.com/mpv-player/mpv-examples/tree/master/libmpv>
- Community D3D11 render API gist (unofficial, not in master): <https://gist.github.com/dragonflylee/244a84cb4e2bff7b25025a7af148c4e2>
- Prebuilt Windows binaries: <https://github.com/zhongfly/mpv-winbuild> (LGPL: `mpv-dev-lgpl-*.7z`)

---

## 13. Recommendations for a Windows wallpaper engine using libmpv

Given the above, the practical choices for a wallpaper engine are:

| Goal | Recommended path |
| --- | --- |
| Simplest: one video, looped, behind icons, no audio | `mpv_create` + `wid=<HWND>` + `vo=gpu-next` + `gpu-api=d3d11` + `loop-file=inf` + `ao=null` + `no-input` + `no-osc` + `idle=yes`. No render context needed. |
| Playlist of videos, smooth cross-fade between them | Same as above, drive `loadfile` on `MPV_EVENT_END_FILE` with reason=EOF. Render context is still optional; useful only if you need to draw an OSD. |
| Compose mpv with other UI / scene graph | OpenGL render API + ANGLE, host's D3D11 device passed to ANGLE via `EGL_D3D11_DEVICE_ANGLE`. Render mpv to an FBO, blit it into your scene. |
| Get mpv pixels into a D3D11 texture (no ANGLE) | Not currently possible from upstream libmpv. Options: (a) patch in a D3D11 render API yourself (rejected PR exists, you can revive it); (b) use the `--d3d11-output-mode=composition` VO mode and use DComp to host the swapchain (merged July 2025, not yet exposed via libmpv); (c) consider LibVLC's DComp integration instead. |
| Closed-source app distribution | Use `mpv-dev-lgpl-*.7z` from mpv-winbuild. Dynamic link only. Bundle the VC++ redistributable. |
| Open-source app distribution | Any mpv-winbuild archive is fine. Consider MSVC-built `mpv-dev-*.7z` for ABI compatibility. |

Final risk notes for the architecture review:
- D3D11 render API is the missing piece. The wallpaper engine's architecture should have an explicit fallback / shim layer so that if you need to switch to a patch-built libmpv (or to LibVLC) in the future, the abstraction above is stable.
- mpv's threading contract is friendly but the "render API + wakeup callback" dance is easy to get wrong on first try. Plan for an integration spike before committing to the final design.
- The `wid` + child-HWND approach loses some control but gains robustness: it works on every GPU mpv supports and survives driver regressions because the same code path mpv.exe uses, you use. For a wallpaper (which doesn't need pixel-level composition), this is the lowest-risk path.
