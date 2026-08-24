const specs = [
  "DirectX 11 Native",
  "NVDEC HW Decode",
  "Intel QuickSync",
  "LibVLC Media Core",
  "WebView2 Runtime",
  "WebGL & HTML5",
  "4K / 8K Displays",
  "Per-Monitor DPI",
  "184 MB Idle RAM",
  ".NET 8 Native",
  "Process Isolation",
  "SHA-256 Verified",
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
      className="ticker overflow-hidden border-y border-border bg-surface-sunken py-3"
      aria-label="Technical specifications"
    >
      <div className="ticker-track flex w-max">
        <TickerRow />
        <TickerRow ariaHidden />
      </div>
    </div>
  );
}
