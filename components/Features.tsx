import Image from "next/image";
import type { ReactNode } from "react";
import { Reveal } from "./Reveal";

function Cell({
  span,
  title,
  detail,
  children,
  delay = 0,
}: {
  span: string;
  title: string;
  detail: string;
  children: ReactNode;
  delay?: number;
}) {
  return (
    <Reveal className={span} delay={delay}>
      <div className="group h-full rounded-2xl border border-border bg-surface p-6 transition-colors duration-200 hover:border-accent-bright/40">
        <h3 className="text-base font-semibold text-foreground">{title}</h3>
        <p className="mt-1 text-sm leading-relaxed text-foreground-muted">
          {detail}
        </p>
        <div className="mt-6">{children}</div>
      </div>
    </Reveal>
  );
}

function Bar({ label, value, width }: { label: string; value: string; width: string }) {
  return (
    <div>
      <div className="flex items-baseline justify-between font-mono text-[11px] uppercase tracking-wide">
        <span className="text-foreground-muted">{label}</span>
        <span className="text-foreground">{value}</span>
      </div>
      <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-foreground/10">
        <div
          className="h-full rounded-full bg-accent-bright/80"
          style={{ width }}
        />
      </div>
    </div>
  );
}

function Chip({
  children,
  active = false,
}: {
  children: ReactNode;
  active?: boolean;
}) {
  return (
    <span
      className={`inline-flex items-center rounded-full border px-3 py-1 font-mono text-[11px] uppercase tracking-wide ${
        active
          ? "border-accent-bright/60 text-accent-bright"
          : "border-border text-foreground-muted"
      }`}
    >
      {children}
    </span>
  );
}

const galleryImages = [
  { src: "/gallery-aurora.png", alt: "Aurora live wallpaper — deep blue light ribbons over night mountains" },
  { src: "/gallery-ocean.png", alt: "Bioluminescent ocean live wallpaper — glowing teal trails in dark water" },
  { src: "/gallery-ember.png", alt: "Volcanic ember live wallpaper — glowing lava cracks through dark rock" },
];

export function Features() {
  return (
    <section
      id="features"
      className="mx-auto max-w-[1400px] px-6 py-24 lg:px-8"
      aria-labelledby="features-heading"
    >
      <Reveal className="max-w-2xl">
        <h2
          id="features-heading"
          className="text-3xl font-semibold tracking-tight text-foreground sm:text-4xl"
        >
          Engineered to stay out of the way.
        </h2>
        <p className="mt-4 text-foreground-muted leading-relaxed">
          Live wallpapers with the overhead of a static one — the engineering
          below is how.
        </p>
      </Reveal>

      <div className="mt-12 grid grid-cols-1 gap-4 lg:grid-cols-6">
        {/* Gaming Auto-Pause — the headline feature */}
        <Cell
          span="lg:col-span-3"
          title="Gaming Auto-Pause"
          detail="Detects full-screen games and 3D apps on any monitor, then suspends rendering entirely — zero FPS cost while you play."
        >
          <div className="flex items-end gap-8">
            <div className="flex-1 space-y-4">
              <Bar label="Desktop idle · GPU" value="11.9%" width="12%" />
              <Bar label="Game detected · GPU" value="0%" width="0%" />
            </div>
            <div className="text-right">
              <p className="font-mono text-5xl font-semibold text-accent-bright">
                0%
              </p>
              <p className="mt-1 font-mono text-[11px] uppercase tracking-wide text-foreground-muted">
                overhead in-game
              </p>
            </div>
          </div>
        </Cell>

        {/* Wallpaper gallery — 3 filled, 4 reserved for the user's own */}
        <Cell
          span="lg:col-span-3"
          title="Any wallpaper you own"
          detail="Video, interactive web, and shader wallpapers through a dual engine — LibVLC for media, WebView2 for HTML5 and WebGL."
          delay={0.05}
        >
          <div className="grid grid-cols-7 gap-2">
            {galleryImages.map((image) => (
              <div
                key={image.src}
                className="aspect-[4/3] overflow-hidden rounded-md border border-border"
              >
                <Image
                  src={image.src}
                  alt={image.alt}
                  width={1200}
                  height={896}
                  className="h-full w-full object-cover"
                />
              </div>
            ))}
            {["04", "05", "06", "07"].map((slot) => (
              <div
                key={slot}
                aria-hidden="true"
                className="flex aspect-[4/3] items-center justify-center rounded-md border border-dashed border-border/70"
              >
                <span className="font-mono text-[9px] uppercase tracking-widest text-foreground-muted/60">
                  Empty
                </span>
              </div>
            ))}
          </div>
          <p className="mt-3 font-mono text-[11px] uppercase tracking-wide text-foreground-muted">
            Slots 04–07 — your wallpapers here
          </p>
        </Cell>

        {/* Multi-Monitor */}
        <Cell
          span="lg:col-span-2"
          title="Multi-Monitor"
          detail="Independent wallpapers per display, or one scene spanned across all of them. Multi-DPI, up to 240 Hz."
        >
          <div>
            <div className="relative h-16 overflow-hidden rounded-md border border-border">
              <div
                aria-hidden="true"
                className="absolute inset-0 bg-[linear-gradient(105deg,#164e63_0%,#0e7490_38%,#7c2d12_100%)]"
              />
              <div
                aria-hidden="true"
                className="absolute inset-y-0 left-1/2 w-[3px] -translate-x-1/2 bg-background"
              />
            </div>
            <div className="mt-2 flex justify-between font-mono text-[10px] uppercase tracking-wide text-foreground-muted">
              <span>Display 1</span>
              <span>Display 2</span>
            </div>
            <div className="mt-3 flex flex-wrap gap-2">
              <Chip active>Span</Chip>
              <Chip>Clone</Chip>
              <Chip>Independent</Chip>
            </div>
          </div>
        </Cell>

        {/* Crash-Resistant Engine */}
        <Cell
          span="lg:col-span-2"
          title="Crash-Resistant Engine"
          detail="Playback runs in an isolated AppRunner process. Close or crash the UI — the desktop never notices."
          delay={0.05}
        >
          <div className="space-y-2 font-mono text-[11px]">
            <div className="flex items-center justify-between rounded-md border border-dashed border-border px-3 py-2">
              <span className="text-foreground-muted">WallpaperTurbo.UI</span>
              <span className="uppercase tracking-wide text-foreground-muted/70">
                closed
              </span>
            </div>
            <div className="flex items-center justify-between rounded-md border border-accent-bright/50 px-3 py-2">
              <span className="text-foreground">AppRunner.exe</span>
              <span className="flex items-center gap-1.5 uppercase tracking-wide text-accent-bright">
                <span
                  aria-hidden="true"
                  className="pulse-dot inline-block h-1.5 w-1.5 rounded-full bg-accent-bright"
                />
                rendering
              </span>
            </div>
          </div>
        </Cell>

        {/* Live Telemetry */}
        <Cell
          span="lg:col-span-2"
          title="Live Telemetry"
          detail="Real CPU, GPU, RAM, and FPS readouts in the Techie dashboard — with Mica and Minimal layouts."
          delay={0.1}
        >
          <div className="space-y-3">
            <Bar label="CPU" value="0.7%" width="7%" />
            <Bar label="GPU" value="11.9%" width="12%" />
            <Bar label="RAM" value="0.18 GB" width="5%" />
          </div>
        </Cell>

        {/* GPU Routing */}
        <Cell
          span="lg:col-span-2"
          title="GPU Routing"
          detail="Pin decoding to the power-saving integrated GPU and keep the dedicated card free for the game."
        >
          <div className="flex flex-wrap gap-2">
            <Chip>Auto</Chip>
            <Chip active>iGPU</Chip>
            <Chip>dGPU</Chip>
          </div>
          <p className="mt-4 font-mono text-[11px] uppercase tracking-wide text-foreground-muted">
            decode → igpu · game → dgpu
          </p>
        </Cell>

        {/* Battery Saver */}
        <Cell
          span="lg:col-span-2"
          title="Battery Saver"
          detail="On battery power, animations throttle or pause automatically to stretch runtime."
          delay={0.05}
        >
          <div className="flex items-center gap-4">
            <div
              aria-hidden="true"
              className="relative h-7 w-16 rounded-md border border-border"
            >
              <div className="absolute inset-1 right-[40%] rounded-sm bg-accent-bright/80" />
              <div className="absolute -right-1.5 top-1/2 h-3 w-1 -translate-y-1/2 rounded-r-sm bg-border" />
            </div>
            <p className="font-mono text-[11px] uppercase tracking-wide text-foreground-muted">
              on battery → throttled
            </p>
          </div>
        </Cell>

        {/* Verified Auto-Updater */}
        <Cell
          span="lg:col-span-2"
          title="Verified Auto-Updater"
          detail="Every update is SHA-256 verified against the GitHub release, with Stable and Beta channels."
          delay={0.1}
        >
          <div className="space-y-3">
            <p className="truncate font-mono text-xs text-foreground-muted">
              076d567392c9…8279c4d2c
            </p>
            <div className="flex flex-wrap gap-2">
              <Chip active>Verified</Chip>
              <Chip>Stable</Chip>
              <Chip>Beta</Chip>
            </div>
          </div>
        </Cell>
      </div>
    </section>
  );
}
