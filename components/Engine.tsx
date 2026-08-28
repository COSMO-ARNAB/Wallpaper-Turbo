"use client";

import { useState } from "react";
import { Reveal } from "./Reveal";

export function Engine() {
  const [active, setActive] = useState<"libvlc" | "webview2" | null>(null);

  return (
    <section id="engine" className="border-y border-[rgba(138,147,166,0.18)] bg-black-level" aria-labelledby="engine-heading">
      <div className="grid-shell py-16">
        <Reveal>
          <h2 id="engine-heading" className="font-display text-[clamp(2.5rem,5vw,4rem)] font-bold uppercase text-backlight">
            Two paths, one layer.
          </h2>
          <p className="mt-4 max-w-[60ch] font-sans text-sm leading-relaxed text-standby">
            Video and web converge on the same wallpaper layer. Four stages, one output.
          </p>
        </Reveal>

        <div className="mt-10 grid grid-cols-1 gap-8 lg:grid-cols-12">
          <div className="lg:col-span-5 font-mono text-xs">
            <div className="space-y-2">
              <div className="flex items-center gap-3">
                <span className="flex h-6 w-6 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-panel-off text-backlight">1</span>
                <span className="text-standby">fetch</span>
              </div>
              <div className="ml-3 h-4 w-px bg-[rgba(138,147,166,0.18)]" />
              <div className="flex items-center gap-3">
                <span className="flex h-6 w-6 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-panel-off text-backlight">2</span>
                <span className="text-standby">decode</span>
              </div>
              <div className="ml-3 h-4 w-px bg-[rgba(138,147,166,0.18)]" />
              <div className="flex items-center gap-3">
                <span className="flex h-6 w-6 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-panel-off text-backlight">3</span>
                <span className="text-standby">composite</span>
              </div>
              <div className="ml-3 h-4 w-px bg-[rgba(138,147,166,0.18)]" />
              <div className="flex items-center gap-3">
                <span className="flex h-6 w-6 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-panel-off text-backlight">4</span>
                <span className="text-standby">scan-out</span>
              </div>
              <div className="ml-3 h-4 w-px bg-[rgba(138,147,166,0.18)]" />
              <div className="font-sans text-sm font-semibold text-signal">wallpaper layer</div>
            </div>
          </div>

          <div className="lg:col-span-7 space-y-3">
            <button
              type="button"
              onClick={() => setActive(active === "libvlc" ? null : "libvlc")}
              className={`w-full border px-4 py-4 text-left font-mono text-xs ${active === "libvlc" || active === null ? "border-signal bg-[rgba(96,205,255,0.08)] text-signal" : "border-[rgba(138,147,166,0.18)] bg-panel-off text-standby"}`}
            >
              <div className="font-semibold">LibVLC</div>
              <div className="mt-1 text-[11px]">MP4 · MKV · WebM — NVDEC / QuickSync / DXVA2</div>
            </button>

            <div className="flex justify-center">
              <span className="h-6 w-px bg-[rgba(138,147,166,0.18)]" />
            </div>

            <button
              type="button"
              onClick={() => setActive(active === "webview2" ? null : "webview2")}
              className={`w-full border px-4 py-4 text-left font-mono text-xs ${active === "webview2" || active === null ? "border-signal bg-[rgba(96,205,255,0.08)] text-signal" : "border-[rgba(138,147,166,0.18)] bg-panel-off text-standby"}`}
            >
              <div className="font-semibold">WebView2</div>
              <div className="mt-1 text-[11px]">HTML5 · CSS · WebGL</div>
            </button>

            <div className="flex justify-center">
              <span className="h-6 w-px bg-[rgba(138,147,166,0.18)]" />
            </div>

            <div className="border border-[rgba(138,147,166,0.18)] bg-panel-off px-4 py-3 text-center font-mono text-xs text-backlight">
              wallpaper layer
            </div>

            <div className="mt-6 border border-tungsten/30 bg-[rgba(255,201,139,0.08)] p-4">
              <p className="font-mono text-xs text-tungsten">Color temperature — night light demo</p>
              <p className="mt-1 font-sans text-xs text-standby">The only Tungsten field outside the OSD.</p>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
