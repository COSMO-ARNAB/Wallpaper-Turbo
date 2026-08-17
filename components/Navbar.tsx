"use client";

import { useEffect, useState } from "react";

const links = [
  { href: "#features", label: "Features" },
  { href: "#engine", label: "Architecture" },
  { href: "#download", label: "Download" },
];

export function Navbar() {
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <>
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-[100] focus:rounded-md focus:bg-accent focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-accent-foreground focus:outline-none focus:ring-2 focus:ring-accent-bright"
      >
        Skip to content
      </a>
      <nav
        className={`fixed top-0 z-50 w-full transition-all duration-200 ${
          scrolled
            ? "tactile-nav"
            : "border-b border-transparent bg-transparent"
        }`}
        aria-label="Primary"
      >
        <div className="mx-auto flex h-16 max-w-[1400px] items-center justify-between px-6 lg:px-8">
          <a
            href="#top"
            className="flex items-center gap-2.5 text-base font-semibold tracking-tight text-foreground transition-opacity hover:opacity-90"
          >
            <span className="flex h-7 w-7 items-center justify-center rounded-lg border border-border-bright bg-surface-raised font-mono text-xs font-bold text-accent-bright">
              WT
            </span>
            <span>Wallpaper Turbo</span>
          </a>

          <div className="flex items-center gap-6">
            <div className="hidden items-center gap-6 sm:flex">
              {links.map((link) => (
                <a
                  key={link.href}
                  href={link.href}
                  className="text-sm font-medium text-foreground-muted transition-colors hover:text-foreground"
                >
                  {link.label}
                </a>
              ))}
            </div>
            <a
              href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
              target="_blank"
              rel="noopener noreferrer"
              className="tactile-raised inline-flex h-9 items-center justify-center gap-2 rounded-md px-3.5 text-xs font-semibold text-foreground transition-all hover:border-border-bright hover:bg-surface-overlay active:scale-[0.98]"
            >
              <span>GitHub</span>
              <span className="text-[10px] text-foreground-muted">↗</span>
            </a>
          </div>
        </div>
      </nav>
    </>
  );
}
