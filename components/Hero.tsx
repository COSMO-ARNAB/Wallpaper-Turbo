"use client";

import { Reveal } from "./Reveal";

const readouts = [
  { value: "3", unit: "", label: "PIPELINES" },
  { value: "184", unit: "MB", label: "IDLE" },
  { value: "0", unit: "", label: "TELEMETRY" },
];

const capabilities = [
  { tag: "VIDEO", formats: "mp4, mkv, webm", path: "NVDEC / QuickSync / DXVA2" },
  { tag: "WEB", formats: "html5, css, webgl", path: "WebView2" },
  { tag: "FX", formats: "shaders", path: "DirectX 11" },
];

export function Hero() {
  return (
    <section
      id="hero"
      className="relative flex min-h-[100dvh] flex-col"
      aria-label="Wallpaper Turbo hero"
    >
      <div className="grid-shell flex flex-1 flex-col justify-center py-16 lg:py-24">
        <div className="grid grid-cols-1 items-center gap-12 lg:grid-cols-12">
          <div className="lg:col-span-6">
            <Reveal>
              <h1 className="font-display text-[clamp(4rem,9vw,7.5rem)] font-extrabold uppercase leading-[0.92] tracking-[-0.01em] text-off-white">
                Runs anything
                <br />
                you throw at it.
              </h1>
            </Reveal>
            <Reveal delay={120}>
              <p className="mt-6 max-w-[52ch] font-sans text-[19px] leading-[1.55] text-machine-grey">
                Set a wallpaper. It renders under your icons and gets out of the
                way when you play.
              </p>
            </Reveal>
            <Reveal delay={200}>
              <div className="mt-8 flex flex-wrap items-center gap-3">
                <a
                  href="#download"
                  className="btn-primary inline-flex h-10 items-center justify-center px-6 font-sans text-[15px] font-semibold"
                >
                  Download for Windows
                </a>
                <a
                  href="#engine"
                  className="inline-flex h-10 items-center justify-center border border-engraved bg-transparent px-6 font-sans text-sm font-medium text-off-white hover:bg-plate"
                >
                  See the pipelines
                </a>
              </div>
            </Reveal>
          </div>

          <Reveal delay={280} className="lg:col-span-6">
            <div className="plate-frame p-6 lg:p-8" role="group" aria-label="Capability plate">
              <p className="font-mono text-[11px] font-medium tracking-[0.12em] text-machine-grey">
                CAPABILITY PLATE / WALLPAPER TURBO
              </p>
              <div className="mt-6 flex gap-10">
                {readouts.map((r) => (
                  <div key={r.label}>
                    <div className="font-display text-[clamp(2.5rem,4vw,3.5rem)] font-bold leading-none text-amber">
                      {r.value}
                      {r.unit && <span className="text-[0.4em] font-medium">{r.unit}</span>}
                    </div>
                    <div className="mt-1 font-mono text-[11px] tracking-[0.12em] text-machine-grey">
                      {r.label}
                    </div>
                  </div>
                ))}
              </div>
              <div className="mt-8 border-t border-engraved pt-5 font-mono text-[13px] leading-[1.9] text-off-white">
                {capabilities.map((c) => (
                  <div key={c.tag} className="flex flex-wrap gap-x-3">
                    <span className="w-14 shrink-0 text-machine-grey">{c.tag}</span>
                    <span>{c.formats}</span>
                    <span className="text-machine-grey">- {c.path}</span>
                  </div>
                ))}
              </div>
            </div>
            <p className="mt-4 font-mono text-xs text-machine-grey">
              icons ▸ SHELLDLL_DefView ▸ wallpaper layer
            </p>
          </Reveal>
        </div>
      </div>
    </section>
  );
}