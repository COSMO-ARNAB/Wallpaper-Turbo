import Link from "next/link";

export function Footer() {
  return (
    <footer className="border-t border-engraved">
      <div className="grid-shell flex flex-col gap-8 py-8 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <p className="font-mono text-xs font-medium tracking-[0.12em] text-off-white">WALLPAPER TURBO</p>
          <p className="mt-1 font-mono text-xs text-machine-grey">
            Model WT-1 for Windows 10 and 11. Open source.
          </p>
        </div>

        <div className="flex flex-col gap-6 lg:flex-row lg:gap-12">
          <nav aria-label="Footer">
            <ul className="flex flex-wrap gap-4 font-mono text-xs text-machine-grey">
              <li><a href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo" target="_blank" rel="noopener noreferrer" className="hover:text-off-white hover:underline hover:decoration-amber hover:underline-offset-2">GitHub</a></li>
              <li><a href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases" target="_blank" rel="noopener noreferrer" className="hover:text-off-white hover:underline hover:decoration-amber hover:underline-offset-2">Releases</a></li>
              <li><Link href="/privacy" className="hover:text-off-white hover:underline hover:decoration-amber hover:underline-offset-2">Privacy</Link></li>
              <li><Link href="/terms" className="hover:text-off-white hover:underline hover:decoration-amber hover:underline-offset-2">Terms</Link></li>
            </ul>
          </nav>

          <div className="plate-frame px-3 py-2 font-mono text-[11px] leading-relaxed text-machine-grey">
            <div>NAMEPLATE WT-1</div>
            <div>Big Shoulders / Archivo / Spline Mono</div>
            <div>#101214 #16181B #2A2E33 #F2F0E9 #6E7681 #FFB454</div>
            <div>build 2026-08-28 / Capability Plate</div>
          </div>
        </div>
      </div>
    </footer>
  );
}