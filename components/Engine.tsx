import { Reveal } from "./Reveal";

const layers = [
  { level: "01", name: "Desktop Icons View", detail: "Shell icons container (SHELLDLL_DefView). Clicks, selection & context menus remain native." },
  { level: "02", name: "Wallpaper Turbo Target", detail: "DirectX 11 / LibVLC hardware canvas injected underneath the icon view.", accent: true },
  { level: "03", name: "WorkerW Host Window", detail: "Native Win32 desktop background host window spawned behind Progman." },
  { level: "04", name: "Windows DWM Compositor", detail: "Desktop Window Manager handles multi-DPI compositing and GPU presentation." },
];

export function Engine() {
  return (
    <section
      id="engine"
      className="relative overflow-hidden border-t border-border bg-background py-24"
      aria-labelledby="engine-heading"
    >
      <div className="pointer-events-none absolute -left-32 top-20 h-96 w-96 rounded-full bg-[radial-gradient(ellipse_at_center,rgba(56,189,248,0.05),transparent_65%)] blur-2xl" aria-hidden="true" />

      <div className="mx-auto grid max-w-[1400px] grid-cols-1 items-center gap-12 px-6 lg:grid-cols-2 lg:gap-16 lg:px-8">
        <Reveal>
          <div>
            <h2 id="engine-heading" className="font-display text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
              Injected directly into the Windows desktop tree.
            </h2>
            <p className="mt-4 max-w-[58ch] text-base leading-relaxed text-foreground-muted">
              Wallpaper Turbo hooks into a WorkerW window spawned behind the Windows Shell icon view. The wallpaper renders underneath your desktop icons without capturing mouse clicks or stealing focus.
            </p>
            <ul className="mt-6 space-y-3">
              {[
                "Native Win32 desktop interop with zero transparent overlay windows",
                "Automatic DWM watchdog restores playback after GPU driver restarts",
                "Subprocess isolation allows updating the UI without restarting the render stream",
              ].map((point) => (
                <li key={point} className="flex items-start gap-3 text-sm leading-relaxed text-foreground-muted">
                  <span aria-hidden="true" className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-accent-bright" />
                  <span>{point}</span>
                </li>
              ))}
            </ul>
          </div>
        </Reveal>

        {/* Z-Axis Cascade — overlapping cards with subtle rotation */}
        <Reveal delay={120}>
          <div className="bezel-shell">
            <div className="bezel-core p-6 sm:p-8">
              <div className="flex items-center justify-between border-b border-border pb-4 font-mono text-xs text-foreground-muted">
                <span>Win32 Z-Order Stack</span>
                <span>Top to Bottom</span>
              </div>

              <div className="mt-6 space-y-3">
                {layers.map((layer, i) => (
                  <div
                    key={layer.level}
                    className={`flex items-start gap-3.5 rounded-xl border p-3.5 transition-all duration-500 ease-[cubic-bezier(0.32,0.72,0,1)] ${
                      layer.accent
                        ? "border-accent-bright/50 bg-accent/15 shadow-[0_4px_16px_rgba(56,189,248,0.12)]"
                        : "border-border bg-surface-sunken"
                    } ${i % 2 === 0 ? "lg:-rotate-[0.6deg]" : "lg:rotate-[0.6deg]"} hover:rotate-0 hover:scale-[1.01]`}
                    style={{ marginTop: i === 0 ? 0 : -4 }}
                  >
                    <span className="font-mono text-xs font-bold tracking-widest text-accent-bright">{layer.level}</span>
                    <div className="min-w-0 flex-1">
                      <p className={`font-mono text-xs font-semibold ${layer.accent ? "text-accent-bright" : "text-foreground"}`}>{layer.name}</p>
                      <p className="mt-1 text-xs leading-relaxed text-foreground-muted">{layer.detail}</p>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
