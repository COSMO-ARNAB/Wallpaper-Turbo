import Image from "next/image";
import { Reveal } from "./Reveal";

const wallpapers = [
  {
    src: "/gallery-aurora.png",
    title: "Aurora Borealis",
    tech: "4K 60FPS Video",
    engine: "LibVLC",
    code: `// HLSL — aurora.frag
float4 aurora(float2 uv){
  float t = _Time.y * 0.06;
  float a = sin(uv.y*6. + t);
  return lerp(colA,colB,a);
}`,
  },
  {
    src: "/gallery-ocean.png",
    title: "Bioluminescent Tide",
    tech: "Interactive WebGL",
    engine: "WebView2",
    code: `// WebView2 — tide.html
<canvas id="tide"></canvas>
<script>import { Tide }
from './tide.frag'</script>`,
  },
  {
    src: "/gallery-ember.png",
    title: "Volcanic Rift",
    tech: "HLSL Shader",
    engine: "WebView2",
    code: `// HLSL — rift.hlsl
uniform float time;
void main(out float4 c
: SV_Target){ c = ember
(uv, time); }`,
  },
];

export function Gallery() {
  return (
    <section
      id="gallery"
      className="border-y border-border bg-surface-sunken/60 py-24"
      aria-labelledby="gallery-heading"
    >
      <div className="mx-auto max-w-[1400px] px-6 lg:px-8">
        <Reveal>
          <div className="flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <div className="eyebrow">Proof, not mockups · Rendered beneath icons</div>
              <h2 id="gallery-heading" className="mt-4 font-display text-4xl font-bold tracking-tighter text-foreground sm:text-5xl">
                Runs anything you throw at it.
              </h2>
            </div>
            <p className="max-w-md font-mono text-xs leading-relaxed text-foreground-muted">
              Video, web, shaders — each shown as <span className="text-foreground">code + result diptych</span> inside hardware bezels. No AI slop, just the engine.
            </p>
          </div>
        </Reveal>

        <div className="mt-12 grid grid-cols-1 gap-6 md:grid-cols-3">
          {wallpapers.map((item, i) => (
            <Reveal key={item.src} delay={80 + i * 100}>
              <div className="bezel-shell">
                <div className="bezel-core overflow-hidden">
                  {/* Diptych: code | result */}
                  <div className="grid grid-cols-2 gap-px bg-border">
                    <div className="bg-[#050507] p-3">
                      <div className="mb-2 flex items-center gap-1.5">
                        <span className="h-2 w-2 rounded-full bg-white/20" />
                        <span className="h-2 w-2 rounded-full bg-white/15" />
                        <span className="h-2 w-2 rounded-full bg-white/10" />
                      </div>
                      <pre className="whitespace-pre-wrap break-words font-mono text-[10px] leading-relaxed text-accent-bright/90">
                        {item.code}
                      </pre>
                    </div>
                    <div className="relative aspect-[4/3] overflow-hidden bg-surface">
                      <Image
                        src={item.src}
                        alt={`${item.title} — ${item.tech} live wallpaper`}
                        fill
                        sizes="(min-width: 768px) 20vw, 100vw"
                        className="object-cover transition-transform duration-700 group-hover:scale-[1.03]"
                      />
                      <div aria-hidden="true" className="absolute inset-0 bg-gradient-to-t from-black/50 via-transparent to-transparent" />
                    </div>
                  </div>

                  <div className="flex items-center justify-between px-4 py-3">
                    <div>
                      <p className="font-display text-sm font-semibold text-foreground">{item.title}</p>
                      <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-foreground-muted">
                        {item.tech}
                      </p>
                    </div>
                    <span className="rounded-full border border-border bg-surface-sunken px-2.5 py-1 font-mono text-[10px] font-semibold text-accent-bright">
                      {item.engine}
                    </span>
                  </div>
                </div>
              </div>
            </Reveal>
          ))}
        </div>

        <Reveal delay={200}>
          <p className="mt-6 text-center font-mono text-[11px] text-foreground-muted">
            Each wallpaper renders <span className="text-foreground">under SHELLDLL_DefView</span> — desktop icons, selection and context menus stay native.
          </p>
        </Reveal>
      </div>
    </section>
  );
}
