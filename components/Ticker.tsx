"use client";

import { useEffect, useRef, useState } from "react";

const specs = [
  "DirectX 11",
  "NVDEC",
  "QuickSync",
  "DXVA2",
  "184 MB idle",
  ".NET 8",
  "LibVLC",
  "WebView2",
  "SHA-256",
];

function TickerRow({ ariaHidden }: { ariaHidden?: boolean }) {
  return (
    <div aria-hidden={ariaHidden} className="flex shrink-0 items-center">
      {specs.map((spec) => (
        <span
          key={spec}
          className="flex items-center whitespace-nowrap font-mono text-xs text-standby"
        >
          <span className="px-6">{spec}</span>
          <span aria-hidden="true" className="h-4 w-px bg-[rgba(138,147,166,0.18)]" />
        </span>
      ))}
    </div>
  );
}

export function Ticker() {
  const ref = useRef<HTMLDivElement>(null);
  const [inView, setInView] = useState(true);
  const [hovered, setHovered] = useState(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const obs = new IntersectionObserver(
      ([entry]) => setInView(entry.isIntersecting),
      { threshold: 0.1 }
    );
    obs.observe(el);
    return () => obs.disconnect();
  }, []);

  const paused = !inView || hovered;

  return (
    <div
      ref={ref}
      className={`ticker overflow-hidden border-y border-[rgba(138,147,166,0.18)] bg-black-level py-3 ${paused ? "ticker-paused" : ""}`}
      aria-label="Technical specifications"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setHovered(true)}
      onBlur={() => setHovered(false)}
    >
      <div className="ticker-track flex w-max">
        <TickerRow />
        <TickerRow ariaHidden />
      </div>
    </div>
  );
}
