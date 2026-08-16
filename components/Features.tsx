const capabilities = [
  {
    name: "Hardware Accelerated",
    detail: "Native GPU video decoding via DirectX, NVDEC, and QuickSync.",
  },
  {
    name: "Dual Media Engine",
    detail: "LibVLC for video plus WebView2 for HTML5, WebGL, and shader wallpapers.",
  },
  {
    name: "4K / 8K Playback",
    detail: "Smooth playback on high-DPI and 144–240 Hz displays.",
  },
  {
    name: "Multi-Monitor",
    detail: "Independent wallpapers per display, plus span and clone modes.",
  },
  {
    name: "Low Resource Usage",
    detail: "Near-zero idle footprint on an optimized .NET 8 core.",
  },
  {
    name: "Gaming Auto-Pause",
    detail: "Detects full-screen apps and drops CPU/GPU overhead to zero.",
  },
  {
    name: "GPU Routing",
    detail: "Route decoding to Auto, Integrated, or Dedicated GPU.",
  },
  {
    name: "Battery Saver",
    detail: "Automatically throttles animations when running on battery.",
  },
  {
    name: "Explorer Recovery",
    detail: "Auto-restores playback after DWM, driver, or shell restarts.",
  },
  {
    name: "Crash-Resistant Playback",
    detail: "Isolated runner process keeps wallpapers alive if the UI crashes.",
  },
  {
    name: "Live Telemetry",
    detail: "Real-time CPU, GPU, RAM, and FPS stats in the Techie dashboard.",
  },
  {
    name: "Verified Auto-Updater",
    detail: "SHA-256 verified updates with Stable and Beta channels.",
  },
];

export function Features() {
  return (
    <section
      id="features"
      className="mx-auto max-w-[1400px] px-6 py-24 lg:px-8"
      aria-labelledby="features-heading"
    >
      <div className="max-w-2xl">
        <h2
          id="features-heading"
          className="text-3xl font-semibold tracking-tight text-foreground sm:text-4xl"
        >
          Features
        </h2>
        <p className="mt-4 text-foreground-muted leading-relaxed">
          Built for Windows 10 and 11, with performance and stability first.
        </p>
      </div>

      <ul className="mt-10 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {capabilities.map((capability, index) => (
          <li
            key={capability.name}
            className="rounded-lg border border-border bg-surface px-5 py-5"
          >
            <span
              aria-hidden="true"
              className="font-mono text-xs text-foreground-muted"
            >
              {String(index + 1).padStart(2, "0")}
            </span>
            <p className="mt-2 font-medium text-foreground">{capability.name}</p>
            <p className="mt-1 text-sm leading-relaxed text-foreground-muted">
              {capability.detail}
            </p>
          </li>
        ))}
      </ul>
    </section>
  );
}
