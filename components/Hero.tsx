import Image from "next/image";

interface HeroProps {
  version?: string | null;
  releaseUrl?: string;
}

const stats = [
  { label: "CPU", value: "0.7%" },
  { label: "GPU", value: "11.9%" },
  { label: "RAM", value: "184 MB" },
  { label: "ENGINE", value: "DirectX 11" },
];

export function Hero({
  version = null,
  releaseUrl = "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases",
}: HeroProps = {}) {
  return (
    <section
      id="top"
      className="relative flex min-h-[100dvh] items-center overflow-hidden"
      aria-label="Wallpaper Turbo hero"
    >
      {/* Full-bleed desktop scene */}
      <div
        aria-hidden="true"
        className="absolute inset-0"
      >
        <Image
          src="/hero-desktop.png"
          alt="Wallpaper Turbo running live on Windows desktop"
          fill
          priority
          sizes="100vw"
          className="object-cover object-center"
        />
      </div>

      {/* Dark scrims for high-contrast readability */}
      <div
        aria-hidden="true"
        className="absolute inset-0 bg-[linear-gradient(to_right,rgba(9,9,11,0.4)_0%,rgba(9,9,11,0.85)_55%,rgba(9,9,11,0.98)_100%)]"
      />
      <div
        aria-hidden="true"
        className="absolute inset-x-0 bottom-0 h-44 bg-[linear-gradient(to_top,var(--background),transparent)]"
      />
      <div
        aria-hidden="true"
        className="absolute inset-x-0 top-0 h-28 bg-[linear-gradient(to_bottom,rgba(9,9,11,0.8),transparent)]"
      />

      <div className="relative z-10 mx-auto flex w-full max-w-[1400px] justify-end px-6 pt-24 pb-24 lg:px-8">
        <div className="max-w-xl">
          <div className="flex items-center gap-2">
            <span className="rounded border border-border-bright bg-surface-raised px-2 py-0.5 font-mono text-[11px] font-semibold text-accent-bright">
              {version ? version : "v1.0.0"}
            </span>
            <span className="font-mono text-xs text-foreground-muted">
              Windows 10 / 11 · 64-bit
            </span>
          </div>

          <h1 className="mt-5 text-5xl font-bold leading-[1.08] tracking-tight text-foreground sm:text-6xl">
            Live wallpapers.
            <br />
            <span className="text-accent-bright">Zero compromise.</span>
          </h1>

          <p className="mt-5 max-w-[46ch] text-lg leading-relaxed text-foreground-muted">
            A native, hardware-accelerated desktop wallpaper engine for Windows.
            Suspends rendering automatically whenever a full-screen game starts.
          </p>

          <div className="mt-8 flex flex-wrap items-center gap-3">
            <a
              href="#download"
              className="inline-flex h-11 items-center justify-center rounded-md bg-accent px-6 text-sm font-semibold text-accent-foreground shadow-sm transition-colors hover:bg-accent/90"
            >
              Download Installer (.exe)
            </a>
            <a
              href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
              target="_blank"
              rel="noopener noreferrer"
              className="tactile-raised inline-flex h-11 items-center justify-center rounded-md px-5 text-sm font-semibold text-foreground transition-colors hover:border-border-bright"
            >
              View Source on GitHub
            </a>
          </div>
        </div>
      </div>

      {/* Live engine telemetry dock */}
      <div className="absolute bottom-8 left-6 z-10 hidden items-center gap-1.5 rounded-lg border border-border bg-surface-sunken/95 p-1.5 shadow-xl md:flex lg:left-8">
        <div className="flex items-center gap-2 rounded bg-surface px-3 py-1 font-mono text-[11px] font-medium text-foreground">
          <span
            aria-hidden="true"
            className="status-indicator"
          />
          TELEMETRY
        </div>
        {stats.map((stat) => (
          <div
            key={stat.label}
            className="rounded bg-surface/40 px-2.5 py-1 font-mono text-[11px] text-foreground-muted"
          >
            {stat.label} <span className="font-semibold text-foreground">{stat.value}</span>
          </div>
        ))}
      </div>
    </section>
  );
}
