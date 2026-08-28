"use client";

import { useState } from "react";
import { Reveal } from "./Reveal";

export function Features() {
  const [monitorMode, setMonitorMode] = useState<"span" | "clone" | "independent">("span");

  return (
    <section id="features" className="border-t border-[rgba(138,147,166,0.18)] bg-panel-off" aria-labelledby="features-heading">
      <div className="grid-shell py-16">
        <Reveal>
          <h2 id="features-heading" className="font-display text-[clamp(2.5rem,5vw,4rem)] font-bold uppercase tracking-tight text-backlight">
            You should not notice it.
          </h2>
        </Reveal>

        <div className="mt-10 border-t border-[rgba(138,147,166,0.18)]">
          {/* Row 1 — Suspends when you play */}
          <div className="grid grid-cols-1 gap-6 border-b border-[rgba(138,147,166,0.18)] py-8 lg:grid-cols-12 lg:gap-8">
            <div className="lg:col-span-5">
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-backlight">Suspends when you play</h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-standby">
                Detects full-screen games and pauses decoding. Your frames stay yours.
              </p>
            </div>
            <div className="lg:col-span-7 flex items-center gap-6 font-mono text-xs">
              <div className="flex-1 border border-[rgba(138,147,166,0.18)] bg-black-level p-4">
                <div className="text-[11px] text-standby">desktop idle</div>
                <div className="mt-1 font-mono text-2xl text-backlight">11.9% <span className="text-xs text-standby">GPU</span></div>
              </div>
              <span className="text-standby">→</span>
              <div className="flex-1 border border-[rgba(138,147,166,0.18)] bg-black-level p-4">
                <div className="text-[11px] text-signal">in game</div>
                <div className="mt-1 font-mono text-2xl text-signal">0.0% <span className="text-xs text-signal/70">GPU</span></div>
              </div>
            </div>
          </div>

          {/* Row 2 — Stays under your icons */}
          <div className="grid grid-cols-1 gap-6 border-b border-[rgba(138,147,166,0.18)] py-8 lg:grid-cols-12 lg:gap-8">
            <div className="lg:col-span-5">
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-backlight">Stays under your icons</h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-standby">
                Rendered into WorkerW behind SHELLDLL_DefView. Icons, selection and menus keep their layer.
              </p>
            </div>
            <div className="lg:col-span-7 border border-[rgba(138,147,166,0.18)] bg-black-level p-4 font-mono text-xs">
              <div className="flex justify-between text-standby"><span>icons</span><span>SHELLDLL_DefView</span></div>
              <div className="my-2 h-px bg-[rgba(138,147,166,0.18)]" />
              <div className="flex justify-between text-signal"><span>wallpaper layer</span><span>WorkerW</span></div>
            </div>
          </div>

          {/* Row 3 — One wallpaper per monitor */}
          <div className="grid grid-cols-1 gap-6 border-b border-[rgba(138,147,166,0.18)] py-8 lg:grid-cols-12 lg:gap-8">
            <div className="lg:col-span-5">
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-backlight">One wallpaper per monitor</h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-standby">
                Span one scene, mirror it, or run something different on each. Mixed refresh and DPI handled.
              </p>
            </div>
            <div className="lg:col-span-7">
              <div className="flex h-20 items-center overflow-hidden border border-[rgba(138,147,166,0.18)] bg-black-level p-2">
                {monitorMode === "span" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center bg-[rgba(96,205,255,0.14)] font-mono text-[11px] text-signal">Display 1 - span</div>
                    <div className="flex h-full flex-1 items-center justify-center bg-[rgba(96,205,255,0.14)] font-mono text-[11px] text-signal">Display 2 - span</div>
                  </div>
                )}
                {monitorMode === "clone" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center bg-[rgba(96,205,255,0.18)] font-mono text-[11px] text-signal">Display 1 - mirrored</div>
                    <div className="flex h-full flex-1 items-center justify-center bg-[rgba(96,205,255,0.18)] font-mono text-[11px] text-signal">Display 2 - mirrored</div>
                  </div>
                )}
                {monitorMode === "independent" && (
                  <div className="flex h-full w-full gap-1.5">
                    <div className="flex h-full flex-1 items-center justify-center bg-signal font-mono text-[11px] text-panel-off">Wallpaper A</div>
                    <div className="flex h-full flex-1 items-center justify-center bg-tungsten font-mono text-[11px] text-panel-off">Wallpaper B</div>
                  </div>
                )}
              </div>
              <div className="mt-3 flex gap-2">
                {(["span", "clone", "independent"] as const).map((mode) => (
                  <button
                    key={mode}
                    type="button"
                    onClick={() => setMonitorMode(mode)}
                    className={`rounded-[2px] border px-3 py-1 font-mono text-xs capitalize ${monitorMode === mode ? "border-signal bg-[rgba(96,205,255,0.12)] text-signal" : "border-[rgba(138,147,166,0.18)] text-standby hover:text-backlight"}`}
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
              <h3 className="font-sans text-[22px] font-semibold leading-tight text-backlight">Keeps running when you close the app</h3>
              <p className="mt-2 max-w-[60ch] font-sans text-sm leading-relaxed text-standby">
                The render host lives in AppRunner. Close the config window and the wallpaper does not blink.
              </p>
            </div>
            <div className="lg:col-span-7 space-y-2 font-mono text-xs">
              <div className="flex items-center justify-between border border-dashed border-[rgba(138,147,166,0.18)] bg-black-level/60 px-4 py-3">
                <span className="text-standby">WallpaperTurbo.UI.exe</span>
                <span className="text-standby">closed</span>
              </div>
              <div className="flex items-center justify-between border border-signal/30 bg-black-level px-4 py-3">
                <span className="flex items-center gap-2 text-backlight"><span className="h-1.5 w-1.5 rounded-full bg-tungsten" /> AppRunner.exe</span>
                <span className="text-signal">running</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
