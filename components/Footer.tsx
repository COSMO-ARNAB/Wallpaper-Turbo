import Link from "next/link";

export function Footer() {
  return (
    <footer className="border-t border-border bg-surface-sunken">
      <div className="mx-auto flex max-w-[1400px] flex-col items-start justify-between gap-6 px-6 py-12 sm:flex-row sm:items-center lg:px-8">
        <div>
          <p className="text-sm font-medium text-foreground">
            © {new Date().getFullYear()} Wallpaper Turbo
          </p>
          <p className="mt-1 font-mono text-[11px] uppercase tracking-wider text-foreground-muted">
            .NET 8 Native | Open Source | Zero Telemetry | Windows 10 / 11
          </p>
        </div>

        <nav aria-label="Footer">
          <ul className="flex flex-wrap items-center gap-6 font-mono text-xs text-foreground-muted">
            <li>
              <Link
                href="/privacy"
                className="transition-colors hover:text-foreground"
              >
                Privacy
              </Link>
            </li>
            <li>
              <Link
                href="/terms"
                className="transition-colors hover:text-foreground"
              >
                Terms
              </Link>
            </li>
            <li>
              <a
                href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
                target="_blank"
                rel="noopener noreferrer"
                className="transition-colors hover:text-foreground"
              >
                GitHub
              </a>
            </li>
            <li>
              <a
                href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo#readme"
                target="_blank"
                rel="noopener noreferrer"
                className="transition-colors hover:text-foreground"
              >
                Docs
              </a>
            </li>
            <li>
              <a
                href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo/blob/main/LICENSE"
                target="_blank"
                rel="noopener noreferrer"
                className="transition-colors hover:text-foreground"
              >
                License
              </a>
            </li>
          </ul>
        </nav>
      </div>
    </footer>
  );
}
