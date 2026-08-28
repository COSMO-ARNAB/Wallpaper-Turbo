"use client";

import { Reveal } from "./Reveal";

interface HeroProps {
  version?: string | null;
  releaseUrl?: string;
}

export function Hero({
  version = null,
  releaseUrl = "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases",
}: HeroProps = {}) {
  return (
    <section
      id="hero"
      className="relative flex min-h-[100dvh] flex-col"
      aria-label="Wallpaper Turbo hero"
    >
      <div className="grid-shell flex flex-1 flex-col justify-center py-16 lg:py-24">
        <Reveal>
          <h1 className="font-display text-[clamp(4rem,9vw,7.5rem)] font-extrabold uppercase leading-[0.92] tracking-[-0.01em] text-backlight">
            Live wallpapers.
            <br />
            Set one.
          </h1>
        </Reveal>
        <Reveal delay={120}>
          <p className="mt-6 max-w-[52ch] font-sans text-[19px] leading-[1.55] text-standby">
            It renders under your icons and gets out of the way when you play.
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
              className="inline-flex h-10 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-transparent px-6 font-sans text-sm font-medium text-backlight hover:bg-black-level"
            >
              See how it works
            </a>
          </div>
        </Reveal>

        <Reveal delay={280}>
          <div className="mt-16 flex items-end gap-6">
            <div className="flex gap-4" aria-hidden="true">
              <div className="flex flex-col items-center gap-1">
                <span className="flex h-10 w-10 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-panel-off text-[10px] text-standby">Bin</span>
                <span className="font-mono text-[11px] text-backlight">Recycle Bin</span>
              </div>
              <div className="flex flex-col items-center gap-1">
                <span className="flex h-10 w-10 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-panel-off text-[10px] text-standby">PC</span>
                <span className="font-mono text-[11px] text-backlight">This PC</span>
              </div>
              <div className="flex flex-col items-center gap-1">
                <span className="flex h-10 w-10 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-panel-off text-[10px] text-standby">▭</span>
                <span className="font-mono text-[11px] text-backlight">Docs</span>
              </div>
            </div>
          </div>
          <p className="mt-6 font-mono text-xs text-standby">
            icons &gt; SHELLDLL_DefView &gt; wallpaper layer
          </p>
        </Reveal>
      </div>
    </section>
  );
}
