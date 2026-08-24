import Image from "next/image";

const wallpapers = [
  {
    src: "/gallery-aurora.png",
    title: "Aurora Borealis",
    tech: "4K 60FPS Video",
    engine: "LibVLC",
  },
  {
    src: "/gallery-ocean.png",
    title: "Bioluminescent Tide",
    tech: "Interactive WebGL",
    engine: "WebView2",
  },
  {
    src: "/gallery-ember.png",
    title: "Volcanic Rift",
    tech: "HLSL Shader",
    engine: "WebView2",
  },
];

export function Gallery() {
  return (
    <section
      id="gallery"
      className="border-y border-border bg-surface-sunken"
      aria-labelledby="gallery-heading"
    >
      {/* Header row — inside the container */}
      <div className="mx-auto flex max-w-[1400px] flex-wrap items-end justify-between gap-4 px-6 pt-20 pb-10 lg:px-8">
        <h2
          id="gallery-heading"
          className="text-4xl font-extrabold tracking-tighter text-foreground sm:text-5xl"
        >
          Runs anything you throw at it.
        </h2>
        <p className="max-w-sm text-sm leading-relaxed text-foreground-muted">
          Video, interactive web, and raw GPU shaders — all rendered as your
          desktop background.
        </p>
      </div>

      {/* Full-bleed image strip — breaks the container on purpose */}
      <div className="grid grid-cols-1 gap-px bg-border md:grid-cols-3">
        {wallpapers.map((item) => (
          <figure key={item.src} className="group relative aspect-[16/10] overflow-hidden bg-surface">
            <Image
              src={item.src}
              alt={`${item.title} — ${item.tech} live wallpaper`}
              fill
              sizes="(min-width: 768px) 33vw, 100vw"
              className="object-cover transition-transform duration-500 group-hover:scale-[1.03]"
            />
            <div
              aria-hidden="true"
              className="absolute inset-0 bg-[linear-gradient(to_top,rgba(9,9,11,0.85)_0%,transparent_50%)]"
            />
            <figcaption className="absolute inset-x-0 bottom-0 flex items-end justify-between p-5">
              <div>
                <p className="text-sm font-bold text-foreground">{item.title}</p>
                <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-foreground-muted">
                  {item.tech}
                </p>
              </div>
              <span className="rounded border border-border-bright bg-background/70 px-2 py-0.5 font-mono text-[10px] font-semibold text-accent-bright">
                {item.engine}
              </span>
            </figcaption>
          </figure>
        ))}
      </div>
    </section>
  );
}
