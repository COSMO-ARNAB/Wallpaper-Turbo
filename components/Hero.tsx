"use client";

import Image from "next/image";
import { Reveal } from "./Reveal";

interface HeroProps {
  version?: string | null;
  releaseUrl?: string;
}

const telemetry = [
  { label: "ENGINE", value: "ON" },
  { label: "CPU", value: "0.7%" },
  { label: "GPU", value: "11.9%" },
  { label: "RAM", value: "184 MB" },
  { label: "RENDERER", value: "DirectX 11" },
];

export function Hero({
  version = null,
  releaseUrl = "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases",
}: HeroProps = {}) {
  const handleDownloadClick = (e: React.MouseEvent<HTMLAnchorElement>) => {
    e.preventDefault();
    const target = document.getElementById("download");
    if (!target) {
      window.location.hash = "download";
      return;
    }
    const prefersReduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (prefersReduced) {
      target.scrollIntoView({ behavior: "auto", block: "start" });
      history.pushState(null, "", "#download");
      return;
    }
    const navOffset = 24;
    const top = target.getBoundingClientRect().top + window.scrollY - navOffset;
    const start = window.scrollY;
    const distance = top - start;
    const duration = 680;
    const ease = (t: number) => 1 - Math.pow(1 - t, 3);
    let startTime: number | null = null;
    const step = (ts: number) => {
      if (startTime === null) startTime = ts;
      const elapsed = ts - startTime;
      const progress = Math.min(elapsed / duration, 1);
      window.scrollTo(0, start + distance * ease(progress));
      if (progress < 1) requestAnimationFrame(step);
      else {
        history.pushState(null, "", "#download");
        target.classList.add("ring-1", "ring-accent-bright/20");
        window.setTimeout(() => target.classList.remove("ring-1", "ring-accent-bright/20"), 1100);
      }
    };
    requestAnimationFrame(step);
  };

  return (
    <section
      id="top"
      className="relative flex min-h-[100dvh] flex-col overflow-hidden bg-background"
      aria-label="Wallpaper Turbo hero"
    >
      {/* Subtle mesh glow — OLED, not flat */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -top-32 left-1/2 h-[700px] w-[900px] -translate-x-1/2 rounded-full bg-[radial-gradient(ellipse_at_center,rgba(56,189,248,0.08),transparent_60%)] blur-3xl"
      />
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -bottom-40 -right-40 h-[600px] w-[600px] rounded-full bg-[radial-gradient(ellipse_at_center,rgba(168,85,247,0.07),transparent_60%)] blur-3xl"
      />

      {/* Nav spacer for floating pill */}
      <div className="h-24 shrink-0" aria-hidden="true" />

      {/* Editorial Split: left type, right hardware */}
      <div className="relative z-10 mx-auto flex w-full max-w-[1400px] flex-1 flex-col gap-12 px-6 pb-10 pt-6 lg:flex-row lg:items-center lg:gap-8 lg:px-8 lg:pb-16">
        {/* Left — massive type */}
        <div className="flex-1 py-4">
          <Reveal>
            <div className="flex items-center gap-3">
              <a
                href={releaseUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="rounded-full border border-border bg-surface-raised px-3 py-1 font-mono text-[11px] font-semibold tracking-wider text-accent-bright transition-colors hover:border-accent-bright/50"
              >
                {version ? version : "v1.0.0"}
              </a>
              <span className="hidden font-mono text-xs text-foreground-muted sm:inline">
                Windows 10 / 11 · 64-bit · Open Source
              </span>
            </div>
          </Reveal>

          <Reveal delay={80}>
            <div className="eyebrow mt-6">.NET 8 Native · Zero Telemetry · WorkerW Injection</div>
          </Reveal>

          <Reveal delay={140}>
            <h1 className="mt-6 font-display text-6xl font-bold leading-[0.88] tracking-tighter text-foreground sm:text-7xl lg:text-[84px]">
              Live wallpapers.
              <br />
              <span className="text-accent-bright">Zero compromise.</span>
            </h1>
          </Reveal>

          <Reveal delay={210}>
            <p className="mt-6 max-w-[52ch] text-[17px] leading-relaxed text-foreground-muted">
              A native, hardware-accelerated wallpaper engine that renders beneath your desktop icons — and suspends itself the instant a game goes full-screen.
            </p>
          </Reveal>

          <Reveal delay={280}>
            <div className="mt-10 flex flex-wrap items-center gap-3">
              <a
                href="#download"
                onClick={handleDownloadClick}
                className="group inline-flex h-[46px] items-center gap-2 rounded-full bg-white px-2 pl-6 font-display text-sm font-semibold text-black transition-all duration-300 hover:bg-white/90 active:scale-[0.98]"
              >
                <span>Download Wallpaper Turbo</span>
                <span className="flex h-8 w-8 items-center justify-center rounded-full bg-black text-white transition-transform duration-300 group-hover:translate-x-0.5 group-hover:-translate-y-0.5">
                  ↗
                </span>
              </a>
              <a
                href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
                target="_blank"
                rel="noopener noreferrer"
                aria-label="View source on GitHub"
                className="group inline-flex h-[46px] items-center gap-2 rounded-full border border-white/10 bg-white/[0.06] px-6 font-display text-sm font-medium text-foreground backdrop-blur transition-all hover:bg-white/[0.10] hover:border-white/15 active:scale-[0.98]"
              >
                <span>View source</span>
                <span className="flex h-6 w-6 items-center justify-center rounded-full bg-white/10 text-[10px] transition-transform duration-300 group-hover:translate-x-0.5 group-hover:-translate-y-0.5">
                  ↗
                </span>
              </a>
            </div>
          </Reveal>

          <Reveal delay={340}>
            <p className="mt-6 font-mono text-xs text-foreground-muted">
              Offline installer · Automatic background updates · SHA-256 verified
            </p>
          </Reveal>
        </div>

        {/* Right — Double-Bezel hardware preview */}
        <Reveal delay={180} className="flex-1 lg:max-w-[560px]">
          <div className="bezel-shell">
            <div className="bezel-core overflow-hidden">
              <div className="relative aspect-[16/10] overflow-hidden bg-[#050507]">
                <Image
                  src="/hero-desktop.png"
                  alt="Wallpaper Turbo running live on Windows desktop"
                  fill
                  priority
                  sizes="(min-width: 1024px) 560px, 100vw"
                  className="object-cover object-center"
                />
                <div aria-hidden="true" className="absolute inset-0 bg-gradient-to-t from-black/40 via-transparent to-transparent" />
                {/* Window chrome hint */}
                <div className="absolute left-3 top-3 flex items-center gap-1.5">
                  <span className="h-2.5 w-2.5 rounded-full bg-white/80" />
                  <span className="h-2.5 w-2.5 rounded-full bg-white/40" />
                  <span className="h-2.5 w-2.5 rounded-full bg-white/20" />
                </div>
                <div className="absolute bottom-3 left-3 right-3 flex items-center justify-between rounded-full border border-white/10 bg-black/55 px-3 py-2 backdrop-blur-xl">
                  <span className="flex items-center gap-2 font-mono text-[10px] text-white">
                    <span className="status-indicator" />
                    Rendering beneath SHELLDLL_DefView
                  </span>
                  <span className="font-mono text-[10px] text-white/60">WorkerW → 11.9% GPU</span>
                </div>
              </div>
              <div className="flex items-center justify-between border-t border-border bg-surface-sunken px-4 py-3">
                <span className="font-mono text-[10px] uppercase tracking-widest text-foreground-muted">
                  Native Win32 · DirectX 11 · LibVLC
                </span>
                <span className="font-mono text-[10px] text-accent-bright">● Live Preview</span>
              </div>
            </div>
          </div>
          <p className="mt-3 text-center font-mono text-[11px] text-foreground-muted">
            Injected into WorkerW — icons stay native, wallpaper never captures clicks
          </p>
        </Reveal>
      </div>

      {/* Status bar — desktop metaphor */}
      <div className="relative z-10 border-t border-border/60 bg-background/60 backdrop-blur-sm">
        <div className="mx-auto flex max-w-[1400px] flex-wrap items-center gap-x-6 gap-y-2 px-6 py-3 lg:px-8">
          <div className="flex items-center gap-2 font-mono text-[11px] font-semibold text-foreground">
            <span aria-hidden="true" className="status-indicator" />
            WALLPAPER TURBO
          </div>
          <div className="hidden h-3 w-px bg-border sm:block" aria-hidden="true" />
          {telemetry.map((item) => (
            <span key={item.label} className="font-mono text-[11px] text-foreground-muted">
              {item.label} <span className="font-semibold text-foreground">{item.value}</span>
            </span>
          ))}
        </div>
      </div>
    </section>
  );
}
