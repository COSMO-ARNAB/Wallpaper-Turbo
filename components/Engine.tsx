import { Reveal } from "./Reveal";

const layers = [
  { name: "Desktop icons", detail: "drag, select, right-click — untouched", top: true },
  { name: "SHELLDLL_DefView", detail: "the shell's icon view" },
  { name: "Wallpaper Turbo render layer", detail: "video · web · shader output", accent: true },
  { name: "WorkerW / Progman", detail: "native Win32 host window" },
];

export function Engine() {
  return (
    <section
      id="engine"
      className="border-t border-border"
      aria-labelledby="engine-heading"
    >
      <div className="mx-auto grid max-w-[1400px] grid-cols-1 items-center gap-12 px-6 py-24 lg:grid-cols-2 lg:gap-16 lg:px-8">
        <Reveal>
          <p className="font-mono text-[11px] uppercase tracking-[0.22em] text-accent-bright">
            Rendering pipeline
          </p>
          <h2
            id="engine-heading"
            className="mt-4 text-3xl font-semibold tracking-tight text-foreground sm:text-4xl"
          >
            It draws where Windows draws the desktop.
          </h2>
          <p className="mt-4 max-w-[58ch] text-foreground-muted leading-relaxed">
            Wallpaper Turbo spawns a native WorkerW layer directly behind the
            shell's icon view, so the wallpaper lives underneath your icons —
            never on top of them, never stealing focus.
          </p>
          <ul className="mt-6 space-y-3">
            {[
              "Native Win32 interop — no overlay window, no input capture",
              "Watchdog restores playback after DWM resets, driver restarts, or monitor changes",
              "Isolated render process keeps the UI free to close, crash, or update",
            ].map((point) => (
              <li key={point} className="flex gap-3 text-sm leading-relaxed text-foreground-muted">
                <span
                  aria-hidden="true"
                  className="mt-2 h-1 w-1 shrink-0 rounded-full bg-accent-bright"
                />
                {point}
              </li>
            ))}
          </ul>
        </Reveal>

        {/* Win32 z-order, top to bottom */}
        <Reveal delay={0.1}>
          <figure
            className="rounded-2xl border border-border bg-surface p-6 sm:p-8"
            aria-label="Diagram of the Windows desktop layer order, showing Wallpaper Turbo's render layer between the shell icon view and the WorkerW host window"
          >
            <figcaption className="font-mono text-[11px] uppercase tracking-[0.18em] text-foreground-muted">
              Win32 z-order — top to bottom
            </figcaption>
            <div className="relative mt-6 space-y-3">
              <div
                aria-hidden="true"
                className="absolute bottom-4 left-[7px] top-4 w-px bg-border"
              />
              {layers.map((layer, index) => (
                <div
                  key={layer.name}
                  className="relative flex items-center gap-4"
                  style={{ paddingLeft: `${index * 14}px` }}
                >
                  <span
                    aria-hidden="true"
                    className={`relative z-10 h-[15px] w-[15px] shrink-0 rounded-full border-2 ${
                      layer.accent
                        ? "border-accent-bright bg-accent-bright/30"
                        : "border-border bg-surface"
                    }`}
                  />
                  <div
                    className={`flex-1 rounded-lg border px-4 py-3 ${
                      layer.accent
                        ? "border-accent-bright/60 bg-accent-bright/5"
                        : "border-border bg-background"
                    } ${layer.top ? "border-dashed" : ""}`}
                  >
                    <p
                      className={`font-mono text-xs ${
                        layer.accent ? "text-accent-bright" : "text-foreground"
                      }`}
                    >
                      {layer.name}
                    </p>
                    <p className="mt-0.5 font-mono text-[10px] text-foreground-muted">
                      {layer.detail}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </figure>
        </Reveal>
      </div>
    </section>
  );
}
