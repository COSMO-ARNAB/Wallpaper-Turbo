"use client";

import { useEffect, useState } from "react";

const links = [
  { href: "#features", label: "Features" },
  { href: "#gallery", label: "Gallery" },
  { href: "#engine", label: "Engine" },
];

export function Navbar() {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (open) document.body.style.overflow = "hidden";
    else document.body.style.overflow = "";
    return () => {
      document.body.style.overflow = "";
    };
  }, [open]);

  return (
    <>
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[100] focus:rounded-[2px] focus:bg-signal focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-panel-off focus:outline-none focus:ring-2 focus:ring-signal"
      >
        Skip to content
      </a>
      <nav
        className="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-[rgba(138,147,166,0.18)] bg-panel-off px-6 lg:px-[clamp(24px,5vw,120px)]"
        aria-label="Primary"
      >
        <div className="flex items-center gap-8">
          <a href="#top" className="font-mono text-xs font-medium tracking-wide text-backlight">
            Wallpaper Turbo
          </a>
          <div className="hidden items-center gap-6 md:flex">
            {links.map((link) => (
              <a
                key={link.href}
                href={link.href}
                className="font-sans text-sm text-standby transition-colors hover:text-backlight"
              >
                {link.label}
              </a>
            ))}
          </div>
        </div>

        <div className="flex items-center gap-3">
          <a
            href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases"
            target="_blank"
            rel="noopener noreferrer"
            className="hidden rounded-[2px] border border-[rgba(138,147,166,0.18)] bg-black-level px-2.5 py-1 font-mono text-xs text-signal md:inline-flex"
          >
            v1.5.1
          </a>
          <a
            href="#download"
            className="btn-primary inline-flex h-8 items-center justify-center px-4 font-sans text-sm font-semibold"
          >
            Download
          </a>
          <button
            type="button"
            aria-label={open ? "Close menu" : "Open menu"}
            aria-expanded={open}
            onClick={() => setOpen((v) => !v)}
            className="flex h-8 w-8 items-center justify-center border border-[rgba(138,147,166,0.18)] bg-black-level text-backlight md:hidden"
          >
            <span className="relative block h-3 w-3">
              <span className={`absolute left-0 top-1/2 h-px w-3 -translate-y-1/2 bg-backlight transition-transform ${open ? "rotate-45" : "-translate-y-1"}`} />
              <span className={`absolute left-0 top-1/2 h-px w-3 -translate-y-1/2 bg-backlight transition-transform ${open ? "-rotate-45" : "translate-y-1"}`} />
            </span>
          </button>
        </div>
      </nav>

      {open && (
        <div className="fixed inset-0 z-40 bg-panel-off px-6 pt-20">
          <div className="flex flex-col gap-6 border-t border-[rgba(138,147,166,0.18)] pt-8">
            {links.map((link) => (
              <a
                key={link.href}
                href={link.href}
                onClick={() => setOpen(false)}
                className="font-display text-4xl font-bold uppercase text-backlight"
              >
                {link.label}
              </a>
            ))}
            <a
              href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
              target="_blank"
              rel="noopener noreferrer"
              className="mt-4 font-mono text-sm text-signal"
            >
              View source on GitHub
            </a>
          </div>
        </div>
      )}
    </>
  );
}
