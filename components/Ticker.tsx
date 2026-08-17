const specs = [
  "DirectX 11",
  "NVDEC",
  "QuickSync",
  "LibVLC",
  "WebView2",
  "WebGL",
  "4K / 8K",
  "240 Hz",
  "Multi-DPI",
  "Multi-Monitor",
  "0.18 GB RAM idle",
  ".NET 8",
  "Smart audio",
  "SHA-256 verified",
];

function TickerRow({ ariaHidden }: { ariaHidden?: boolean }) {
  return (
    <div
      aria-hidden={ariaHidden}
      className="flex shrink-0 items-center"
    >
      {specs.map((spec) => (
        <span
          key={spec}
          className="flex items-center whitespace-nowrap font-mono text-xs uppercase tracking-[0.18em] text-foreground-muted"
        >
          <span className="px-6">{spec}</span>
          <span aria-hidden="true" className="text-accent-bright/70">
            //
          </span>
        </span>
      ))}
    </div>
  );
}

export function Ticker() {
  return (
    <div
      className="ticker overflow-hidden border-y border-border bg-surface/60 py-3.5"
      aria-label="Technical specifications"
    >
      <div className="ticker-track flex w-max">
        <TickerRow />
        <TickerRow ariaHidden />
      </div>
    </div>
  );
}
