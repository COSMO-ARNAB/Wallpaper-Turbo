"use client";

import { useEffect, useState } from "react";

const links = [
  { href: "#engine", label: "Pipelines" },
  { href: "#features", label: "Measurements" },
];

interface NavbarProps {
  version?: string | null;
}

export function Navbar({ version }: NavbarProps = {}) {
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
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[100] focus:rounded-[2px] focus:bg-amber focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-graphite focus:outline-none focus:ring-2 focus:ring-amber"
      >
        Skip to content
      </a>
      <nav
        className="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-engraved bg-graphite px-6 lg:px-[clamp(24px,5vw,120px)]"
        aria-label="Primary"
      >
        <div className="flex items-center gap-8">
          <a href="#top" className="font-mono text-xs font-medium tracking-[0.12em] text-off-white uppercase">
            Wallpaper Turbo
          </a>
          <div className="hidden items-center gap-6 md:flex">
            {links.map((link) => (
              <a
                key={link.href}
                href={link.href}
                className="font-sans text-sm text-machine-grey transition-colors hover:text-off-white"
              >
                {link.label}
              </a>
            ))}
          </div>
        </div>

        <div className="flex items-center gap-3">
          <span className="hidden rounded-[2px] border border-engraved bg-plate px-2.5 py-1 font-mono text-xs text-amber md:inline-flex">
            {version ?? "v1.5.2"}
          </span>
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
            className="flex h-8 w-8 items-center justify-center border border-engraved bg-plate text-off-white md:hidden"
          >
            <span className="relative block h-3 w-3">
              <span className={`absolute left-0 top-1/2 h-px w-3 -translate-y-1/2 bg-off-white transition-transform ${open ? "rotate-45" : "-translate-y-1"}`} />
              <span className={`absolute left-0 top-1/2 h-px w-3 -translate-y-1/2 bg-off-white transition-transform ${open ? "-rotate-45" : "translate-y-1"}`} />
            </span>
          </button>
        </div>
      </nav>

      {open && (
        <div className="fixed inset-0 z-40 bg-graphite px-6 pt-20">
          <div className="flex flex-col gap-6 border-t border-engraved pt-8">
            {links.map((link) => (
              <a
                key={link.href}
                href={link.href}
                onClick={() => setOpen(false)}
                className="font-display text-4xl font-bold uppercase text-off-white"
              >
                {link.label}
              </a>
            ))}
            <a
              href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
              target="_blank"
              rel="noopener noreferrer"
              className="mt-4 font-mono text-sm text-amber"
            >
              View source on GitHub
            </a>
          </div>
        </div>
      )}
    </>
  );
}