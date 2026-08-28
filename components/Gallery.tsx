"use client";

import Image from "next/image";
import { useState } from "react";
import { Reveal } from "./Reveal";

const wallpapers = [
  {
    src: "/gallery-aurora.png",
    title: "Aurora Borealis",
    tech: "4K 60fps video — LibVLC",
    code: `WallpaperTurbo.exe --wallpaper aurora.mp4 --decode nvdec`,
  },
  {
    src: "/gallery-ocean.png",
    title: "Bioluminescent Tide",
    tech: "Interactive WebGL — WebView2",
    code: `<canvas id="tide"></canvas> // tide.frag + WebView2`,
  },
  {
    src: "/gallery-ember.png",
    title: "Volcanic Rift",
    tech: "HLSL shader — WebView2",
    code: `uniform float time; // rift.hlsl -> wallpaper layer`,
  },
];

function WallpaperCard({ item }: { item: (typeof wallpapers)[number] }) {
  const [open, setOpen] = useState(false);
  return (
    <figure className="w-full">
      <div className="relative aspect-[16/10] w-full overflow-hidden bg-black-level">
        <Image
          src={item.src}
          alt={`${item.title} — ${item.tech}`}
          fill
          sizes="100vw"
          className="object-cover"
        />
      </div>
      <figcaption className="flex flex-wrap items-center justify-between gap-4 border-y border-[rgba(138,147,166,0.18)] bg-panel-off px-6 py-3 lg:px-[clamp(24px,5vw,120px)]">
        <div>
          <h3 className="font-sans text-[22px] font-semibold leading-tight text-backlight">{item.title}</h3>
          <p className="mt-1 font-mono text-xs text-standby">{item.tech}</p>
        </div>
        <button
          type="button"
          onClick={() => setOpen(!open)}
          aria-expanded={open}
          className="border border-[rgba(138,147,166,0.18)] bg-black-level px-3 py-1.5 font-mono text-xs text-standby hover:text-backlight"
        >
          {open ? "hide code" : "code"}
        </button>
      </figcaption>
      {open && (
        <div className="border-b border-[rgba(138,147,166,0.18)] bg-black-level px-6 py-3 lg:px-[clamp(24px,5vw,120px)]">
          <pre className="whitespace-pre-wrap break-words font-mono text-xs text-standby">{item.code}</pre>
        </div>
      )}
    </figure>
  );
}

export function Gallery() {
  return (
    <section id="gallery" aria-labelledby="gallery-heading" className="bg-panel-off">
      <div className="grid-shell py-12">
        <Reveal>
          <h2 id="gallery-heading" className="font-display text-[clamp(2.5rem,5vw,4rem)] font-bold uppercase text-backlight">
            Runs anything you throw at it.
          </h2>
        </Reveal>
      </div>

      <div className="flex flex-col">
        {wallpapers.map((item, i) => (
          <Reveal key={item.src} delay={i * 60}>
            <WallpaperCard item={item} />
          </Reveal>
        ))}
      </div>
    </section>
  );
}
