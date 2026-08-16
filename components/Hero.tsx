import Image from "next/image";

export function Hero() {
  return (
    <section
      id="top"
      className="relative flex min-h-[100dvh] flex-col items-center justify-center px-6 pt-24 pb-16 lg:px-8"
    >
      <div className="mx-auto max-w-[1400px] text-center">
        <h1 className="text-4xl font-semibold tracking-tight text-foreground sm:text-5xl md:text-6xl">
          Wallpaper Turbo
        </h1>

        <p className="mx-auto mt-5 max-w-[50ch] text-lg leading-relaxed text-foreground-muted sm:text-xl">
          Lightweight, hardware-accelerated live wallpapers for Windows.
        </p>

        <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
          <a
            href="#download"
            className="inline-flex h-11 min-w-[10rem] items-center justify-center rounded-md bg-accent px-6 text-sm font-semibold text-accent-foreground transition-colors hover:bg-accent/90 active:scale-[0.98]"
          >
            Download for Windows
          </a>
          <a
            href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex h-11 min-w-[10rem] items-center justify-center rounded-md bg-surface px-6 text-sm font-semibold text-foreground ring-1 ring-border transition-colors hover:bg-foreground/5 active:scale-[0.98]"
          >
            View on GitHub
          </a>
        </div>
      </div>

      <div className="mx-auto mt-12 w-full max-w-[1100px] px-2 sm:px-0">
        <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-sm">
          <Image
            src="/screenshot.png"
            alt="Wallpaper Turbo application window showing the current live wallpaper, engine status metrics, and quick-action controls."
            width={1920}
            height={1080}
            priority
            className="h-auto w-full"
          />
        </div>
      </div>
    </section>
  );
}
