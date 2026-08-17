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
      className="border-t border-border bg-surface-sunken/40"
      aria-labelledby="engine-heading"
    >
      <div className="mx-auto grid max-w-[1400px] grid-cols-1 items-center gap-12 px-6 py-24 lg:grid-cols-2 lg:gap-16 lg:px-8">
        <div>
          <h2
            id="engine-heading"
            className="text-3xl font-bold tracking-tight text-foreground sm:text-4xl"
          >
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
                <span
                  aria-hidden="true"
                  className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-accent-bright"
                />
                <span>{point}</span>
              </li>
            ))}
          </ul>
        </div>

        {/* Win32 z-order hierarchy */}
        <div
          className="tactile-card rounded-xl p-6 sm:p-8"
          aria-label="Diagram of the Windows desktop layer order"
        >
          <div className="flex items-center justify-between border-b border-border pb-4 font-mono text-xs text-foreground-muted">
            <span>Win32 Z-Order Stack</span>
            <span>Top to Bottom</span>
          </div>

          <div className="mt-4 space-y-2.5">
            {layers.map((layer) => (
              <div
                key={layer.level}
                className={`flex items-start gap-3.5 rounded-lg border p-3.5 transition-colors ${
                  layer.accent
                    ? "border-accent-bright/60 bg-accent/15"
                    : "border-border bg-surface-sunken"
                }`}
              >
                <span className="font-mono text-xs font-bold text-accent-bright">
                  {layer.level}
                </span>
                <div className="min-w-0 flex-1">
                  <p
                    className={`font-mono text-xs font-semibold ${
                      layer.accent ? "text-accent-bright" : "text-foreground"
                    }`}
                  >
                    {layer.name}
                  </p>
                  <p className="mt-1 text-xs text-foreground-muted">
                    {layer.detail}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
