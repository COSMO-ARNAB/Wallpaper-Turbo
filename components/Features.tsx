"use client";

import { useState } from "react";
import { Reveal } from "./Reveal";

export function Features() {
  const [monitorMode, setMonitorMode] = useState<"span" | "clone" | "independent">("span");

  return (
    <section
      id="features"
      className="border-t border-engraved"
      aria-labelledby="features-heading"
    >
      <div className="grid-shell py-16 lg:py-24">
        <Reveal>
          <h2
            id="features-heading"
            className="font-display text-[clamp(2.5rem,5vw,4rem)] font-bold uppercase leading-[0.95] text-off-white"
          >
            You should not notice it.
          </h2>
          <p className="mt-4 max-w-[60ch] font-sans text-[17px] leading-[1.55] text-machine-grey">
            Numbers in amber were measured on a real desktop, not estimated.
          </p>
        </Reveal>

        <div className="mt-12 border-t border-engraved">
          {/* Row 1 — Suspends when you play */}
          <div className="grid grid-cols-1 gap-6 border-b border-engraved py-8 lg:grid-cols-12 lg:gap-8">
            <div className="lg:col-span-5">
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-off-white">
                Suspends when you play
              </h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-machine-grey">
                Detects full-screen games and pauses decoding. Your frames stay yours.
              </p>
            </div>
            <div className="flex items-center gap-4 font-mono text-xs lg:col-span-7">
              <div className="flex-1 plate-frame p-4">
                <div className="text-[11px] tracking-[0.12em] text-machine-grey">DESKTOP IDLE</div>
                <div className="mt-1 font-display text-3xl font-bold text-amber">
                  11.9%<span className="ml-1 font-mono text-xs font-normal text-machine-grey">GPU</span>
                </div>
              </div>
              <span aria-hidden="true" className="text-machine-grey">→</span>
              <div className="flex-1 plate-frame p-4">
                <div className="text-[11px] tracking-[0.12em] text-machine-grey">IN GAME</div>
                <div className="mt-1 font-display text-3xl font-bold text-amber">
                  0.0%<span className="ml-1 font-mono text-xs font-normal text-machine-grey">GPU</span>
                </div>
              </div>
            </div>
          </div>

          {/* Row 2 — Stays under your icons */}
          <div className="grid grid-cols-1 gap-6 border-b border-engraved py-8 lg:grid-cols-12 lg:gap-8">
            <div className="lg:col-span-5">
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-off-white">
                Stays under your icons
              </h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-machine-grey">
                Rendered into WorkerW behind SHELLDLL_DefView. Icons, selection
                and menus keep their layer.
              </p>
            </div>
            <div className="plate-frame p-4 font-mono text-xs lg:col-span-7">
              <div className="flex justify-between text-off-white">
                <span>icons</span>
                <span>SHELLDLL_DefView</span>
              </div>
              <div className="my-2 h-px bg-engraved" />
              <div className="flex justify-between text-amber">
                <span>wallpaper layer</span>
                <span>WorkerW</span>
              </div>
            </div>
          </div>

          {/* Row 3 — One wallpaper per monitor */}
          <div className="grid grid-cols-1 gap-6 border-b border-engraved py-8 lg:grid-cols-12 lg:gap-8">
            <div className="lg:col-span-5">
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-off-white">
                One wallpaper per monitor
              </h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-machine-grey">
                Span one scene, mirror it, or run something different on each.
                Mixed refresh and DPI handled.
              </p>
            </div>
            <div className="lg:col-span-7">
              <div className="flex h-20 items-center overflow-hidden plate-frame p-2">
                {monitorMode === "span" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center border border-engraved bg-graphite font-mono text-[11px] text-off-white">
                      Display 1 - span
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center border border-engraved bg-graphite font-mono text-[11px] text-off-white">
                      Display 2 - span
                    </div>
                  </div>
                )}
                {monitorMode === "clone" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center border border-engraved bg-plate font-mono text-[11px] text-off-white">
                      Display 1 - mirrored
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center border border-engraved bg-plate font-mono text-[11px] text-off-white">
                      Display 2 - mirrored
                    </div>
                  </div>
                )}
                {monitorMode === "independent" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center border border-amber bg-plate font-mono text-[11px] text-amber">
                      Wallpaper A
                    </div>
                    <div className="flex h-full flex-1 items-center justify-center border border-engraved bg-graphite font-mono text-[11px] text-off-white">
                      Wallpaper B
                    </div>
                  </div>
                )}
              </div>
              <div className="mt-3 flex gap-2" role="group" aria-label="Multi-monitor mode">
                {(["span", "clone", "independent"] as const).map((mode) => (
                  <button
                    key={mode}
                    type="button"
                    onClick={() => setMonitorMode(mode)}
                    aria-pressed={monitorMode === mode}
                    className={`rounded-[2px] border px-3 py-1 font-mono text-xs capitalize transition-colors ${
                      monitorMode === mode
                        ? "border-machine-grey bg-plate text-off-white"
                        : "border-engraved text-machine-grey hover:text-off-white"
                    }`}
                  >
                    {mode === "independent" ? "separate" : mode}
                  </button>
                ))}
              </div>
            </div>
          </div>

          {/* Row 4 — Keeps running when you close the app */}
          <div className="grid grid-cols-1 gap-6 py-8 lg:grid-cols-12 lg:gap-8">
            <div className="lg:col-span-5">
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-off-white">
                Keeps running when you close the app
              </h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-machine-grey">
                The render host lives in AppRunner. Close the config window and
                the wallpaper does not blink.
              </p>
            </div>
            <div className="space-y-2 font-mono text-xs lg:col-span-7">
              <div className="flex items-center justify-between rounded-[2px] border border-dashed border-engraved px-4 py-3">
                <span className="text-machine-grey">WallpaperTurbo.UI.exe</span>
                <span className="text-machine-grey">closed</span>
              </div>
              <div className="flex items-center justify-between plate-frame px-4 py-3">
                <span className="flex items-center gap-2 text-off-white">
                  <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-amber" />
                  AppRunner.exe
                </span>
                <span className="text-amber">running</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}