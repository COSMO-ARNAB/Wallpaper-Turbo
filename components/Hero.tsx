"use client";

import Image from "next/image";
import { useRef } from "react";
import { motion, useReducedMotion, useScroll, useTransform } from "motion/react";

interface HeroProps {
  version?: string | null;
  releaseUrl?: string;
}

const stats = [
  { label: "CPU", value: "0.7%" },
  { label: "GPU", value: "11.9%" },
  { label: "RAM", value: "0.18 GB" },
];

export function Hero({
  version = null,
  releaseUrl = "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases",
}: HeroProps = {}) {
  const reduceMotion = useReducedMotion();
  const sectionRef = useRef<HTMLElement>(null);

  const { scrollYProgress } = useScroll({
    target: sectionRef,
    offset: ["start start", "end start"],
  });
  const backdropY = useTransform(scrollYProgress, [0, 1], ["0%", "14%"]);

  return (
    <section
      ref={sectionRef}
      id="top"
      className="relative flex min-h-[100dvh] items-center overflow-hidden"
      aria-label="Wallpaper Turbo hero"
    >
      {/* Full-bleed desktop scene: the real app rendering a live wallpaper */}
      <motion.div
        aria-hidden="true"
        className="absolute inset-0 scale-[1.06]"
        style={reduceMotion ? undefined : { y: backdropY }}
      >
        <Image
          src="/hero-desktop.png"
          alt=""
          fill
          priority
          sizes="100vw"
          className="object-cover object-center"
        />
      </motion.div>

      {/* Scrims: right side for text legibility, bottom to blend into page */}
      <div
        aria-hidden="true"
        className="absolute inset-0 bg-[linear-gradient(to_right,color-mix(in_srgb,var(--background)_25%,transparent)_0%,color-mix(in_srgb,var(--background)_72%,transparent)_52%,color-mix(in_srgb,var(--background)_94%,transparent)_100%)]"
      />
      <div
        aria-hidden="true"
        className="absolute inset-x-0 bottom-0 h-40 bg-[linear-gradient(to_top,var(--background),transparent)]"
      />
      <div
        aria-hidden="true"
        className="absolute inset-x-0 top-0 h-24 bg-[linear-gradient(to_bottom,color-mix(in_srgb,var(--background)_70%,transparent),transparent)]"
      />

      {/* Entrance animation is CSS-driven (.hero-enter-*), so SSR output is
          never hidden for reduced-motion users. */}
      <div className="relative z-10 mx-auto flex w-full max-w-[1400px] justify-end px-6 pt-24 pb-24 lg:px-8">
        <div className="max-w-xl">
          <a
            href={releaseUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="glass hero-enter hero-enter-1 inline-flex items-center gap-2 rounded-full px-4 py-1.5 font-mono text-xs tracking-wide text-foreground transition-colors hover:border-accent-bright/60"
          >
            <span
              aria-hidden="true"
              className="pulse-dot inline-block h-1.5 w-1.5 rounded-full bg-accent-bright"
            />
            {version ? `${version} — LATEST STABLE` : "LATEST STABLE"}
          </a>

          <h1 className="hero-enter hero-enter-2 mt-6 text-5xl font-semibold leading-[1.05] tracking-tight text-foreground sm:text-6xl">
            Your desktop,
            <br />
            <em className="text-accent-bright">rendered live.</em>
          </h1>

          <p className="hero-enter hero-enter-3 mt-5 max-w-[46ch] text-lg leading-relaxed text-foreground-muted">
            Lightweight, hardware-accelerated live wallpapers for Windows.
            Pauses itself when you play.
          </p>

          <div className="hero-enter hero-enter-4 mt-8 flex flex-wrap items-center gap-3">
            <a
              href="#download"
              className="inline-flex h-11 items-center justify-center rounded-md bg-accent px-6 text-sm font-semibold text-accent-foreground transition-colors hover:bg-accent/90 active:scale-[0.98]"
            >
              Download for Windows
            </a>
            <a
              href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
              target="_blank"
              rel="noopener noreferrer"
              className="glass inline-flex h-11 items-center justify-center rounded-md px-6 text-sm font-semibold text-foreground transition-colors hover:border-accent-bright/60 active:scale-[0.98]"
            >
              View on GitHub
            </a>
          </div>
        </div>
      </div>

      {/* Live engine telemetry, echoing the app's own status bar */}
      <div className="hero-enter hero-enter-5 absolute bottom-8 left-6 z-10 hidden items-center gap-2 md:flex lg:left-8">
        <div className="glass flex items-center gap-2 rounded-full px-4 py-2 font-mono text-xs text-foreground">
          <span
            aria-hidden="true"
            className="pulse-dot inline-block h-1.5 w-1.5 rounded-full bg-accent-bright"
          />
          ENGINE ON
        </div>
        {stats.map((stat) => (
          <div
            key={stat.label}
            className="glass rounded-full px-4 py-2 font-mono text-xs text-foreground-muted"
          >
            {stat.label} <span className="text-foreground">{stat.value}</span>
          </div>
        ))}
      </div>
    </section>
  );
}
