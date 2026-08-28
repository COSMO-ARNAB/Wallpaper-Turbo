import Link from "next/link";

export function Footer() {
  return (
    <footer className="border-t border-[rgba(138,147,166,0.18)] bg-panel-off">
      <div className="grid-shell flex flex-col gap-8 py-8 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <p className="font-mono text-xs font-medium text-backlight">Wallpaper Turbo</p>
          <p className="mt-1 font-mono text-xs text-standby">
            Model WT-1 · Windows 10/11 · Open source
          </p>
        </div>

        <div className="flex flex-col gap-6 lg:flex-row lg:gap-12">
          <nav aria-label="Footer">
            <ul className="flex flex-wrap gap-4 font-mono text-xs text-standby">
              <li><a href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo" target="_blank" rel="noopener noreferrer" className="hover:text-backlight hover:underline hover:decoration-signal hover:underline-offset-2">GitHub</a></li>
              <li><a href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases" target="_blank" rel="noopener noreferrer" className="hover:text-backlight hover:underline hover:decoration-signal hover:underline-offset-2">Releases</a></li>
              <li><Link href="/privacy" className="hover:text-backlight hover:underline hover:decoration-signal hover:underline-offset-2">Privacy</Link></li>
              <li><Link href="/terms" className="hover:text-backlight hover:underline hover:decoration-signal hover:underline-offset-2">Terms</Link></li>
            </ul>
          </nav>

          <div className="border border-[rgba(138,147,166,0.18)] bg-black-level px-3 py-2 font-mono text-[11px] leading-relaxed text-standby">
            <div>EDID · WT-1 · 1440 · Big Shoulders / Archivo / Spline Mono</div>
            <div>#05070D #0C1220 #E9EDF3 #8A93A6 #60CDFF #FFC98B</div>
            <div>build 2026-08-28 · Panel Off</div>
          </div>
        </div>
      </div>
    </footer>
  );
}
