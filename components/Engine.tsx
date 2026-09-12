import { Reveal } from "./Reveal";

const pipelines = [
  {
    name: "VIDEO",
    formats: "mp4, mkv, webm",
    path: "NVDEC / QuickSync / DXVA2",
    engine: "LibVLC",
  },
  {
    name: "WEB",
    formats: "html5, css, webgl",
    path: "Chromium renderer",
    engine: "WebView2",
  },
  {
    name: "FX",
    formats: "shaders",
    path: "feature level 11_0",
    engine: "DirectX 11",
  },
];

const stages = ["fetch", "decode", "composite", "scan-out"];

export function Engine() {
  return (
    <section
      id="engine"
      className="border-y border-engraved bg-plate"
      aria-labelledby="engine-heading"
    >
      <div className="grid-shell py-16 lg:py-24">
        <Reveal>
          <h2
            id="engine-heading"
            className="font-display text-[clamp(2.5rem,5vw,4rem)] font-bold uppercase leading-[0.95] text-off-white"
          >
            Three pipelines.
            <br />
            One layer.
          </h2>
          <p className="mt-4 max-w-[60ch] font-sans text-[17px] leading-[1.55] text-machine-grey">
            Video, web pages, and shaders all converge on the same wallpaper
            layer. Four stages, one output.
          </p>
        </Reveal>

        <div className="mt-12 grid grid-cols-1 gap-12 lg:grid-cols-12">
          <Reveal delay={100} className="lg:col-span-7">
            <div className="plate-frame overflow-hidden">
              <p className="border-b border-engraved px-5 py-3 font-mono text-[11px] font-medium tracking-[0.12em] text-machine-grey">
                PIPELINE SPECIFICATION
              </p>
              <table className="w-full font-mono text-[13px]">
                <thead className="sr-only">
                  <tr>
                    <th scope="col">Pipeline</th>
                    <th scope="col">Formats</th>
                    <th scope="col">Engine</th>
                    <th scope="col">Decode path</th>
                  </tr>
                </thead>
                <tbody>
                  {pipelines.map((p) => (
                    <tr key={p.name} className="border-b border-engraved last:border-b-0">
                      <th scope="row" className="px-5 py-4 text-left align-top font-medium tracking-[0.08em] text-machine-grey">
                        {p.name}
                      </th>
                      <td className="px-5 py-4 align-top text-off-white">{p.formats}</td>
                      <td className="hidden px-5 py-4 align-top text-off-white sm:table-cell">{p.engine}</td>
                      <td className="px-5 py-4 align-top text-machine-grey">{p.path}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Reveal>

          <Reveal delay={180} className="lg:col-span-5">
            <div className="plate-frame p-5">
              <p className="font-mono text-[11px] font-medium tracking-[0.12em] text-machine-grey">
                STAGES
              </p>
              <ol className="mt-4 font-mono text-[13px]">
                {stages.map((stage, i) => (
                  <li key={stage} className="flex items-center gap-3 border-b border-engraved py-2.5 last:border-b-0">
                    <span className="w-6 text-machine-grey">{String(i + 1).padStart(2, "0")}</span>
                    <span className="text-off-white">{stage}</span>
                  </li>
                ))}
                <li className="flex items-center gap-3 py-2.5">
                  <span className="w-6 text-machine-grey">→</span>
                  <span className="font-medium text-amber">wallpaper layer</span>
                </li>
              </ol>
            </div>
            <p className="mt-4 font-mono text-xs text-machine-grey">
              z-order: below SHELLDLL_DefView · above desktop background
            </p>
          </Reveal>
        </div>
      </div>
    </section>
  );
}