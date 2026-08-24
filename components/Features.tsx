"use client";

import { useState } from "react";

const engines = [
  {
    name: "LibVLC Core",
    formats: "MP4 · MKV · WebM · AVI",
    detail: "Hardware decode via NVDEC / QuickSync / DXVA2",
  },
  {
    name: "WebView2 Runtime",
    formats: "HTML5 · CSS · WebGL",
    detail: "Interactive shader and canvas wallpapers",
  },
];

export function Features() {
  const [monitorMode, setMonitorMode] = useState<"span" | "clone" | "independent">("span");

  return (
    <section
      id="features"
      className="mx-auto max-w-[1400px] px-6 py-24 lg:px-8"
      aria-labelledby="features-heading"
    >
      <div className="max-w-2xl">
        <h2
          id="features-heading"
          className="text-4xl font-extrabold tracking-tighter text-foreground sm:text-5xl"
        >
          Engineered to disappear.
        </h2>
        <p className="mt-4 text-base leading-relaxed text-foreground-muted">
          Every architectural decision serves one goal: you should never notice
          it running.
        </p>
      </div>

      <div className="mt-16 border-t border-border">
        {/* 01 — Gaming Suspension */}
        <div className="grid grid-cols-1 gap-8 border-b border-border py-14 lg:grid-cols-12 lg:gap-12">
          <div className="lg:col-span-1">
            <span className="font-mono text-sm font-bold text-accent-bright">01</span>
          </div>
          <div className="lg:col-span-5">
            <h3 className="text-xl font-bold tracking-tight text-foreground">
              Automatic Gaming Suspension
            </h3>
            <p className="mt-3 text-sm leading-relaxed text-foreground-muted">
              Detects full-screen 3D games and hardware-accelerated applications
              across any connected monitor, immediately suspending video decoding
              and WebGL rendering. Zero FPS tax while you play.
            </p>
          </div>
          <div className="lg:col-span-6">
            <div className="rounded-lg border border-border bg-surface-sunken p-5">
              <div className="grid grid-cols-2 gap-6">
                <div className="border-r border-border pr-6">
                  <span className="font-mono text-[10px] uppercase tracking-wider text-foreground-muted">
                    Desktop Idle
                  </span>
                  <p className="mt-1.5 font-mono text-3xl font-bold text-foreground">
                    11.9<span className="text-sm font-normal text-foreground-muted">% GPU</span>
                  </p>
                  <p className="mt-1 text-xs text-foreground-muted">
                    Hardware decode active
                  </p>
                </div>
                <div>
                  <span className="font-mono text-[10px] uppercase tracking-wider text-accent-bright">
                    In-Game State
                  </span>
                  <p className="mt-1.5 font-mono text-3xl font-bold text-accent-bright">
                    0.0<span className="text-sm font-normal text-accent-bright/70">% GPU</span>
                  </p>
                  <p className="mt-1 text-xs text-foreground-muted">
                    Rendering fully halted
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* 02 — Multi-Monitor */}
        <div className="grid grid-cols-1 gap-8 border-b border-border py-14 lg:grid-cols-12 lg:gap-12">
          <div className="lg:col-span-1">
            <span className="font-mono text-sm font-bold text-accent-bright">02</span>
          </div>
          <div className="lg:col-span-5">
            <h3 className="text-xl font-bold tracking-tight text-foreground">
              Multi-Monitor & High Refresh
            </h3>
            <p className="mt-3 text-sm leading-relaxed text-foreground-muted">
              Mixed refresh rates up to 240 Hz, independent per-monitor DPI
              scaling, and three display topologies — span a single scene across
              all screens, mirror it, or run something different on each.
            </p>
          </div>
          <div className="lg:col-span-6">
            <div className="space-y-4">
              <div className="flex h-20 items-center overflow-hidden rounded-lg border border-border bg-surface-sunken p-2">
                {monitorMode === "span" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center rounded bg-accent/20 font-mono text-[11px] text-accent-bright">
                      Display 1 (Span Left)
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center rounded bg-accent/20 font-mono text-[11px] text-accent-bright">
                      Display 2 (Span Right)
                    </div>
                  </div>
                )}
                {monitorMode === "clone" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center rounded bg-accent/25 font-mono text-[11px] text-accent-bright">
                      Display 1 (Mirrored)
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center rounded bg-accent/25 font-mono text-[11px] text-accent-bright">
                      Display 2 (Mirrored)
                    </div>
                  </div>
                )}
                {monitorMode === "independent" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center rounded bg-sky-950 font-mono text-[11px] text-sky-400">
                      Wallpaper A
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center rounded bg-amber-950 font-mono text-[11px] text-amber-400">
                      Wallpaper B
                    </div>
                  </div>
                )}
              </div>
              <div className="flex items-center justify-between">
                <div className="flex gap-2">
                  {(["span", "clone", "independent"] as const).map((mode) => (
                    <button
                      key={mode}
                      type="button"
                      onClick={() => setMonitorMode(mode)}
                      className={`rounded border px-3 py-1 font-mono text-xs capitalize transition-colors ${
                        monitorMode === mode
                          ? "border-accent-bright bg-accent/20 text-accent-bright"
                          : "border-border bg-surface text-foreground-muted hover:text-foreground"
                      }`}
                    >
                      {mode === "independent" ? "Separate" : mode}
                    </button>
                  ))}
                </div>
                <span className="font-mono text-xs text-foreground-muted">
                  Multi-DPI Ready
                </span>
              </div>
            </div>
          </div>
        </div>

        {/* 03 — Process Isolation */}
        <div className="grid grid-cols-1 gap-8 border-b border-border py-14 lg:grid-cols-12 lg:gap-12">
          <div className="lg:col-span-1">
            <span className="font-mono text-sm font-bold text-accent-bright">03</span>
          </div>
          <div className="lg:col-span-5">
            <h3 className="text-xl font-bold tracking-tight text-foreground">
              Isolated Process Architecture
            </h3>
            <p className="mt-3 text-sm leading-relaxed text-foreground-muted">
              The render engine lives in its own AppRunner worker process. Close
              the UI, crash the config window, apply an update — the wallpaper
              on your desktop never blinks.
            </p>
          </div>
          <div className="lg:col-span-6">
            <div className="space-y-2.5 font-mono text-xs">
              <div className="flex items-center justify-between rounded border border-dashed border-border bg-surface-sunken/40 px-4 py-3">
                <span className="text-foreground-muted">WallpaperTurbo.UI.exe</span>
                <span className="text-[11px] text-foreground-muted">
                  Config Window (Closed)
                </span>
              </div>
              <div className="flex items-center justify-between rounded border border-border-bright bg-surface-raised px-4 py-3">
                <div className="flex items-center gap-2">
                  <span className="h-2 w-2 rounded-full bg-accent-bright" />
                  <span className="font-semibold text-foreground">AppRunner.exe</span>
                </div>
                <span className="text-[11px] font-semibold text-accent-bright">
                  Active Render Host
                </span>
              </div>
            </div>
          </div>
        </div>

        {/* 04 — Dual Render Pipeline */}
        <div className="grid grid-cols-1 gap-8 border-b border-border py-14 lg:grid-cols-12 lg:gap-12">
          <div className="lg:col-span-1">
            <span className="font-mono text-sm font-bold text-accent-bright">04</span>
          </div>
          <div className="lg:col-span-5">
            <h3 className="text-xl font-bold tracking-tight text-foreground">
              Dual Render Pipeline
            </h3>
            <p className="mt-3 text-sm leading-relaxed text-foreground-muted">
              Two independent engines under one roof: LibVLC for
              hardware-accelerated video, and Microsoft Edge WebView2 for
              interactive HTML5, CSS, and WebGL shader wallpapers.
            </p>
          </div>
          <div className="lg:col-span-6">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {engines.map((engine) => (
                <div
                  key={engine.name}
                  className="rounded-lg border border-border bg-surface-sunken p-4"
                >
                  <p className="font-mono text-xs font-semibold text-foreground">
                    {engine.name}
                  </p>
                  <p className="mt-2 font-mono text-[10px] uppercase tracking-wider text-accent-bright">
                    {engine.formats}
                  </p>
                  <p className="mt-2 text-xs leading-relaxed text-foreground-muted">
                    {engine.detail}
                  </p>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
