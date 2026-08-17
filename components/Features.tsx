"use client";

import Image from "next/image";
import { useState } from "react";

const galleryImages = [
  { src: "/gallery-aurora.png", title: "Aurora Borealis", desc: "4K 60FPS Video" },
  { src: "/gallery-ocean.png", title: "Bioluminescent Tide", desc: "Interactive WebGL" },
  { src: "/gallery-ember.png", title: "Volcanic Rift", desc: "HLSL Shader" },
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
          className="text-3xl font-bold tracking-tight text-foreground sm:text-4xl"
        >
          Engineered for performance and stability.
        </h2>
        <p className="mt-3 text-base leading-relaxed text-foreground-muted">
          Wallpaper Turbo is architected to run silently in the background with zero impact on gaming frame rates or desktop responsiveness.
        </p>
      </div>

      <div className="mt-12 grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Pillar 1: Gaming Auto-Pause */}
        <div className="tactile-card flex flex-col justify-between rounded-xl p-6 sm:p-8">
          <div>
            <h3 className="text-lg font-semibold tracking-tight text-foreground">
              Automatic Gaming Suspension
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-foreground-muted">
              Detects full-screen 3D games and hardware-accelerated applications across any connected monitor, immediately suspending video decoding and WebGL rendering.
            </p>
          </div>

          <div className="mt-8 rounded-lg border border-border bg-surface-sunken p-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="border-r border-border pr-4">
                <span className="font-mono text-[10px] uppercase tracking-wider text-foreground-muted">
                  Desktop Idle
                </span>
                <p className="mt-1 font-mono text-2xl font-bold text-foreground">
                  11.9% <span className="text-xs font-normal text-foreground-muted">GPU</span>
                </p>
                <p className="mt-1 text-xs text-foreground-muted">
                  Hardware decode active
                </p>
              </div>

              <div>
                <span className="font-mono text-[10px] uppercase tracking-wider text-accent-bright">
                  In-Game State
                </span>
                <p className="mt-1 font-mono text-2xl font-bold text-accent-bright">
                  0.0% <span className="text-xs font-normal text-accent-bright/70">GPU</span>
                </p>
                <p className="mt-1 text-xs text-foreground-muted">
                  Rendering fully halted
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* Pillar 2: Multi-Display Engine */}
        <div className="tactile-card flex flex-col justify-between rounded-xl p-6 sm:p-8">
          <div>
            <h3 className="text-lg font-semibold tracking-tight text-foreground">
              Multi-Monitor & High Refresh
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-foreground-muted">
              Full support for mixed refresh rates up to 240 Hz and independent per-monitor DPI scaling on Windows 10 and 11.
            </p>
          </div>

          <div className="mt-8 space-y-4">
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

        {/* Pillar 3: Process Isolation */}
        <div className="tactile-card flex flex-col justify-between rounded-xl p-6 sm:p-8">
          <div>
            <h3 className="text-lg font-semibold tracking-tight text-foreground">
              Isolated Process Architecture
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-foreground-muted">
              The wallpaper rendering engine runs in an isolated AppRunner worker process. You can close or update the configuration UI without interrupting desktop playback.
            </p>
          </div>

          <div className="mt-8 space-y-2.5 font-mono text-xs">
            <div className="flex items-center justify-between rounded border border-dashed border-border bg-surface-sunken/40 px-3.5 py-2.5">
              <span className="text-foreground-muted">WallpaperTurbo.UI.exe</span>
              <span className="text-[11px] text-foreground-muted">
                Config Window (Closed)
              </span>
            </div>
            <div className="flex items-center justify-between rounded border border-border-bright bg-surface-raised px-3.5 py-2.5">
              <div className="flex items-center gap-2">
                <span className="h-2 w-2 rounded-full bg-accent-bright" />
                <span className="font-semibold text-foreground">AppRunner.exe</span>
              </div>
              <span className="text-[11px] font-semibold text-accent-bright">
                Active Render Host (PID 8412)
              </span>
            </div>
          </div>
        </div>

        {/* Pillar 4: Media Engine */}
        <div className="tactile-card flex flex-col justify-between rounded-xl p-6 sm:p-8">
          <div>
            <h3 className="text-lg font-semibold tracking-tight text-foreground">
              Dual Render Pipeline
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-foreground-muted">
              Built on LibVLC for hardware-accelerated video playback (MP4, MKV, WebM) and Microsoft Edge WebView2 for HTML5, CSS animations, and WebGL shaders.
            </p>
          </div>

          <div className="mt-8 grid grid-cols-3 gap-3">
            {galleryImages.map((item) => (
              <div
                key={item.src}
                className="overflow-hidden rounded-lg border border-border bg-surface-sunken"
              >
                <div className="aspect-[4/3] w-full overflow-hidden">
                  <Image
                    src={item.src}
                    alt={item.title}
                    width={800}
                    height={600}
                    className="h-full w-full object-cover"
                  />
                </div>
                <div className="p-2.5">
                  <p className="truncate text-xs font-semibold text-foreground">
                    {item.title}
                  </p>
                  <p className="font-mono text-[10px] text-foreground-muted">
                    {item.desc}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
