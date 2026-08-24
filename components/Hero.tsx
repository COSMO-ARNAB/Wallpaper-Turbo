import Image from "next/image";

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
  return (
    <section
      id="top"
      className="relative flex min-h-[100dvh] flex-col overflow-hidden"
      aria-label="Wallpaper Turbo hero"
    >
      {/* Full-bleed desktop scene */}
      <div aria-hidden="true" className="absolute inset-0">
        <Image
          src="/hero-desktop.png"
          alt="Wallpaper Turbo running live on Windows desktop"
          fill
          priority
          sizes="100vw"
          className="object-cover object-center"
        />
      </div>

      {/* Scrims: heavy left for text, bottom fade into page */}
      <div
        aria-hidden="true"
        className="absolute inset-0 bg-[linear-gradient(to_right,rgba(9,9,11,0.94)_0%,rgba(9,9,11,0.82)_45%,rgba(9,9,11,0.25)_100%)]"
      />
      <div
        aria-hidden="true"
        className="absolute inset-x-0 bottom-0 h-48 bg-[linear-gradient(to_top,var(--background),transparent)]"
      />
      <div
        aria-hidden="true"
        className="absolute inset-x-0 top-0 h-28 bg-[linear-gradient(to_bottom,rgba(9,9,11,0.8),transparent)]"
      />

      {/* Nav spacer */}
      <div className="h-16 shrink-0" aria-hidden="true" />

      {/* Main content — left-aligned, breaking the usual centered pattern */}
      <div className="relative z-10 flex flex-1 items-center">
        <div className="mx-auto w-full max-w-[1400px] px-6 py-16 lg:px-8">
          <div className="max-w-4xl">
            <div className="flex items-center gap-3">
              <a
                href={releaseUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="rounded border border-border-bright bg-surface-raised px-2.5 py-1 font-mono text-[11px] font-semibold text-accent-bright transition-colors hover:border-accent-bright/60"
              >
                {version ? version : "v1.0.0"}
              </a>
              <span className="font-mono text-xs text-foreground-muted">
                Windows 10 / 11 · 64-bit · Open Source
              </span>
            </div>

            <h1 className="mt-8 text-6xl font-extrabold leading-[0.92] tracking-tighter text-foreground sm:text-7xl lg:text-8xl">
              Live wallpapers.
              <br />
              <span className="text-accent-bright">Zero compromise.</span>
            </h1>

            <p className="mt-6 max-w-[52ch] text-lg leading-relaxed text-foreground-muted">
              A native, hardware-accelerated wallpaper engine that renders
              beneath your desktop icons — and suspends itself the instant a
              game goes full-screen.
            </p>

            <div className="mt-10 flex flex-wrap items-center gap-3">
              <a
                href="#download"
                className="inline-flex h-12 items-center justify-center rounded-md bg-accent px-7 text-sm font-bold text-accent-foreground transition-colors hover:bg-accent/90 active:scale-[0.98]"
              >
                Download Installer
              </a>
              <a
                href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
                target="_blank"
                rel="noopener noreferrer"
                className="tactile-raised inline-flex h-12 items-center justify-center rounded-md px-6 text-sm font-semibold text-foreground transition-colors hover:border-border-bright"
              >
                View Source
              </a>
            </div>
          </div>
        </div>
      </div>

      {/* Status bar — the desktop metaphor, spanning full width */}
      <div className="relative z-10 border-t border-border/60 bg-background/60">
        <div className="mx-auto flex max-w-[1400px] flex-wrap items-center gap-x-6 gap-y-2 px-6 py-3 lg:px-8">
          <div className="flex items-center gap-2 font-mono text-[11px] font-semibold text-foreground">
            <span aria-hidden="true" className="status-indicator" />
            WALLPAPER TURBO
          </div>
          <div className="hidden h-3 w-px bg-border sm:block" aria-hidden="true" />
          {telemetry.map((item) => (
            <span
              key={item.label}
              className="font-mono text-[11px] text-foreground-muted"
            >
              {item.label}{" "}
              <span className="font-semibold text-foreground">
                {item.value}
              </span>
            </span>
          ))}
        </div>
      </div>
    </section>
  );
}
