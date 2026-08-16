"use client";

import { useEffect, useState } from "react";

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
        className="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-50 focus:rounded-md focus:bg-accent focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-accent-foreground focus:shadow-lg focus:outline-none"
      >
        Skip to main content
      </a>
      <nav
        className={`fixed top-0 z-50 w-full border-b transition-colors duration-200 ${
          scrolled
            ? "border-border bg-background"
            : "border-transparent bg-transparent"
        }`}
        aria-label="Primary"
      >
        <div className="mx-auto flex h-16 max-w-[1400px] items-center justify-between px-6 lg:px-8">
          <a
            href="#top"
            className="text-lg font-semibold tracking-tight text-foreground"
          >
            Wallpaper Turbo
          </a>

          <div className="flex items-center gap-6">
            <a
              href="#download"
              className="text-sm font-medium text-foreground-muted transition-colors hover:text-foreground"
            >
              Download
            </a>
            <a
              href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex h-9 items-center justify-center rounded-md bg-surface px-4 text-sm font-medium text-foreground ring-1 ring-border transition-colors hover:bg-foreground/5"
            >
              GitHub
            </a>
          </div>
        </div>
      </nav>
    </>
  );
}
