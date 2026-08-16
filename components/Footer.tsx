export function Footer() {
  return (
    <footer className="mx-auto max-w-[1400px] px-6 py-12 lg:px-8">
      <div className="flex flex-col items-start justify-between gap-6 sm:flex-row sm:items-center">
        <div className="flex flex-col gap-1">
          <p className="text-sm text-foreground-muted">
            © {new Date().getFullYear()} Wallpaper Turbo
          </p>
          <p className="text-xs text-foreground-muted/80">
            Open Source under MIT License · Free &amp; Private (No tracking)
          </p>
        </div>

        <nav aria-label="Footer">
          <ul className="flex flex-wrap items-center gap-6 text-sm font-medium text-foreground-muted">
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
