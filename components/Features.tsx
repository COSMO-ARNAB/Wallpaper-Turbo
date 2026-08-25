"use client";

import { useState } from "react";
import { Reveal } from "./Reveal";

const engines = [
  {
    name: "LibVLC Core",
    formats: "MP4, MKV, WebM, AVI",
    detail: "Hardware decode via NVDEC / QuickSync / DXVA2",
  },
  {
    name: "WebView2 Runtime",
    formats: "HTML5, CSS, WebGL",
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
      <Reveal>
        <div className="max-w-2xl">
          <h2
            id="features-heading"
            className="font-display text-4xl font-bold tracking-tighter text-foreground sm:text-5xl"
          >
            Engineered to disappear.
          </h2>
          <p className="mt-4 max-w-[65ch] text-base leading-relaxed text-foreground-muted">
            Every architectural decision serves one goal: you should never notice it running.
          </p>
        </div>
      </Reveal>

      {/* Asymmetrical Bento */}
      <div className="mt-14 grid grid-cols-1 gap-6 lg:grid-cols-12 lg:gap-6">
        {/* 01 — Gaming Suspension — large */}
        <Reveal delay={80} className="lg:col-span-7">
          <div className="bezel-shell h-full">
            <div className="bezel-core flex h-full flex-col p-6 sm:p-7">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <span className="font-mono text-xs font-bold tracking-widest text-accent-bright">01 — GAMING</span>
                  <h3 className="mt-2 font-display text-xl font-semibold tracking-tight text-foreground">
                    Automatic Gaming Suspension
                  </h3>
                  <p className="mt-2 max-w-[46ch] text-sm leading-relaxed text-foreground-muted">
                    Detects full-screen 3D games across any monitor and suspends decoding + WebGL instantly. Zero FPS tax while you play.
                  </p>
                </div>
                <span className="hidden h-8 w-8 items-center justify-center rounded-full border border-border bg-surface-sunken font-mono text-xs font-bold text-foreground-muted sm:flex">
                  01
                </span>
              </div>

              <div className="mt-6 rounded-xl border border-border bg-surface-sunken p-4">
                <div className="grid grid-cols-2 gap-6">
                  <div className="border-r border-border pr-6">
                    <span className="font-mono text-[10px] uppercase tracking-wider text-foreground-muted">Desktop Idle</span>
                    <p className="mt-1.5 font-mono text-3xl font-bold text-foreground">
                      11.9<span className="text-sm font-normal text-foreground-muted">% GPU</span>
                    </p>
                    <p className="mt-1 font-mono text-[11px] text-foreground-muted">Hardware decode active</p>
                  </div>
                  <div>
                    <span className="font-mono text-[10px] uppercase tracking-wider text-accent-bright">In-Game State</span>
                    <p className="mt-1.5 font-mono text-3xl font-bold text-accent-bright">
                      0.0<span className="text-sm font-normal text-accent-bright/70">% GPU</span>
                    </p>
                    <p className="mt-1 font-mono text-[11px] text-foreground-muted">Rendering fully halted</p>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </Reveal>

        {/* 02 — Multi-Monitor */}
        <Reveal delay={140} className="lg:col-span-5">
          <div className="bezel-shell h-full">
            <div className="bezel-core flex h-full flex-col p-6 sm:p-7">
              <span className="font-mono text-xs font-bold tracking-widest text-accent-bright">02 — MULTI-MONITOR</span>
              <h3 className="mt-2 font-display text-xl font-semibold tracking-tight text-foreground">Multi-Monitor & High Refresh</h3>
              <p className="mt-2 text-sm leading-relaxed text-foreground-muted">
                Mixed refresh up to 240 Hz, independent DPI, three topologies.
              </p>

              <div className="mt-5 flex h-24 items-center overflow-hidden rounded-xl border border-border bg-surface-sunken p-2">
                {monitorMode === "span" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center rounded-lg bg-accent/20 font-mono text-[11px] font-semibold text-accent-bright">
                      Display 1 — Span
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center rounded-lg bg-accent/20 font-mono text-[11px] font-semibold text-accent-bright">
                      Display 2 — Span
                    </div>
                  </div>
                )}
                {monitorMode === "clone" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center rounded-lg bg-accent/25 font-mono text-[11px] font-semibold text-accent-bright">
                      Display 1 — Mirrored
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center rounded-lg bg-accent/25 font-mono text-[11px] font-semibold text-accent-bright">
                      Display 2 — Mirrored
                    </div>
                  </div>
                )}
                {monitorMode === "independent" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center rounded-lg bg-sky-950 font-mono text-[11px] font-semibold text-sky-400">
                      Wallpaper A
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center rounded-lg bg-amber-950 font-mono text-[11px] font-semibold text-amber-400">
                      Wallpaper B
                    </div>
                  </div>
                )}
              </div>

              <div className="mt-4 flex items-center justify-between">
                <div className="flex gap-1.5">
                  {(["span", "clone", "independent"] as const).map((mode) => (
                    <button
                      key={mode}
                      type="button"
                      onClick={() => setMonitorMode(mode)}
                      className={`rounded-full border px-3 py-1 font-mono text-xs capitalize transition-all duration-300 ${monitorMode === mode ? "border-accent-bright bg-accent/20 text-accent-bright" : "border-border bg-surface text-foreground-muted hover:text-foreground hover:border-border-bright"}`}
                    >
                      {mode === "independent" ? "Separate" : mode}
                    </button>
                  ))}
                </div>
                <span className="font-mono text-[10px] uppercase tracking-wider text-foreground-muted">Multi-DPI Ready</span>
              </div>
            </div>
          </div>
        </Reveal>

        {/* 03 — Process Isolation */}
        <Reveal delay={100} className="lg:col-span-5">
          <div className="bezel-shell h-full">
            <div className="bezel-core flex h-full flex-col p-6 sm:p-7">
              <span className="font-mono text-xs font-bold tracking-widest text-accent-bright">03 — ISOLATION</span>
              <h3 className="mt-2 font-display text-xl font-semibold tracking-tight text-foreground">Isolated Process Architecture</h3>
              <p className="mt-2 text-sm leading-relaxed text-foreground-muted">
                Render engine lives in AppRunner. Close the UI — wallpaper never blinks.
              </p>

              <div className="mt-6 space-y-2.5 font-mono text-xs">
                <div className="flex items-center justify-between rounded-xl border border-dashed border-border bg-surface-sunken/40 px-4 py-3">
                  <span className="text-foreground-muted">WallpaperTurbo.UI.exe</span>
                  <span className="rounded-full border border-border bg-surface px-2 py-0.5 text-[11px] text-foreground-muted">Config — Closed</span>
                </div>
                <div className="flex items-center justify-between rounded-xl border border-accent-bright/30 bg-accent/10 px-4 py-3 shadow-[inset_0_1px_1px_rgba(56,189,248,0.15)]">
                  <div className="flex items-center gap-2">
                    <span className="h-2 w-2 rounded-full bg-accent-bright shadow-[0_0_8px_rgba(56,189,248,0.6)]" />
                    <span className="font-semibold text-foreground">AppRunner.exe</span>
                  </div>
                  <span className="font-semibold text-accent-bright">Active Render Host</span>
                </div>
              </div>
            </div>
          </div>
        </Reveal>

        {/* 04 — Dual Pipeline — large */}
        <Reveal delay={160} className="lg:col-span-7">
          <div className="bezel-shell h-full">
            <div className="bezel-core flex h-full flex-col p-6 sm:p-7">
              <span className="font-mono text-xs font-bold tracking-widest text-accent-bright">04 — PIPELINE</span>
              <h3 className="mt-2 font-display text-xl font-semibold tracking-tight text-foreground">Dual Render Pipeline</h3>
              <p className="mt-2 text-sm leading-relaxed text-foreground-muted">
                Two engines, one roof: LibVLC for hardware video, WebView2 for HTML5, CSS and WebGL shaders.
              </p>

              <div className="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2">
                {engines.map((engine) => (
                  <div
                    key={engine.name}
                    className="group rounded-xl border border-border bg-surface-sunken p-4 transition-colors hover:border-border-bright hover:bg-surface"
                  >
                    <p className="font-mono text-xs font-semibold text-foreground">{engine.name}</p>
                    <p className="mt-2 font-mono text-[10px] uppercase tracking-wider text-accent-bright">{engine.formats}</p>
                    <p className="mt-2 text-xs leading-relaxed text-foreground-muted">{engine.detail}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
