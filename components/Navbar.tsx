"use client";

import { useEffect, useState } from "react";

const links = [
  { href: "#features", label: "Features" },
  { href: "#engine", label: "Architecture" },
  { href: "#download", label: "Download" },
];

export function Navbar() {
  const [scrolled, setScrolled] = useState(false);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

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
        className="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-[100] focus:rounded-md focus:bg-accent focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-accent-foreground focus:outline-none focus:ring-2 focus:ring-accent-bright"
      >
        Skip to content
      </a>

      {/* Fluid Island — floating pill, not edge-to-edge */}
      <div className="pointer-events-none fixed inset-x-0 top-0 z-50 flex justify-center px-4 pt-6">
        <nav
          aria-label="Primary"
          className={`pointer-events-auto flex h-[52px] w-full max-w-3xl items-center justify-between rounded-full border px-2 transition-all duration-700 ease-[cubic-bezier(0.32,0.72,0,1)] ${
            scrolled
              ? "border-white/[0.08] bg-[#0a0a0c]/80 backdrop-blur-2xl shadow-[inset_0_1px_1px_rgba(255,255,255,0.07),0_8px_32px_rgba(0,0,0,0.45)]"
              : "border-white/[0.06] bg-[#0a0a0c]/60 backdrop-blur-xl shadow-[inset_0_1px_1px_rgba(255,255,255,0.05),0_8px_24px_rgba(0,0,0,0.35)]"
          } ${open ? "opacity-0 pointer-events-none scale-95" : "opacity-100 scale-100"}`}
        >
          <a
            href="#top"
            className="flex items-center gap-2.5 pl-1 text-sm font-semibold tracking-tight text-foreground transition-opacity hover:opacity-80"
          >
            <span className="flex h-8 w-8 items-center justify-center rounded-full bg-white text-[11px] font-bold tracking-tighter text-black">
              WT
            </span>
            <span className="hidden font-display text-[13px] tracking-tight sm:block">Wallpaper Turbo</span>
          </a>

          <div className="flex items-center gap-1">
            <div className="hidden items-center gap-1 sm:flex">
              {links.map((link) => (
                <a
                  key={link.href}
                  href={link.href}
                  className="rounded-full px-3.5 py-2 font-mono text-xs font-medium text-foreground-muted transition-colors hover:bg-white/[0.06] hover:text-foreground"
                >
                  {link.label}
                </a>
              ))}
            </div>

            <a
              href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
              target="_blank"
              rel="noopener noreferrer"
              aria-label="View source on GitHub"
              className="group hidden h-8 items-center gap-2 rounded-full bg-white px-4 font-display text-xs font-semibold text-black transition-all hover:bg-white/90 active:scale-[0.97] sm:inline-flex"
            >
              <span>GitHub</span>
              <span className="flex h-5 w-5 items-center justify-center rounded-full bg-black/10 text-[10px] leading-none transition-transform duration-300 group-hover:translate-x-0.5 group-hover:-translate-y-0.5">
                ↗
              </span>
            </a>

            {/* Hamburger — morph to X */}
            <button
              type="button"
              aria-label={open ? "Close menu" : "Open menu"}
              aria-expanded={open}
              onClick={() => setOpen((v) => !v)}
              className="relative flex h-8 w-8 items-center justify-center rounded-full bg-white/10 text-foreground backdrop-blur sm:hidden"
            >
              <span
                className={`absolute h-px w-4 bg-white transition-all duration-300 ease-[cubic-bezier(0.32,0.72,0,1)] ${open ? "rotate-45" : "-translate-y-1.5"}`}
              />
              <span
                className={`absolute h-px w-4 bg-white transition-all duration-300 ease-[cubic-bezier(0.32,0.72,0,1)] ${open ? "-rotate-45" : "translate-y-1.5"}`}
              />
            </button>
          </div>
        </nav>
      </div>

      {/* Expanded overlay — glass morph */}
      <div
        aria-hidden={!open}
        className={`fixed inset-0 z-40 flex flex-col bg-[#050507]/85 backdrop-blur-3xl transition-all duration-700 ease-[cubic-bezier(0.32,0.72,0,1)] ${open ? "opacity-100 visible" : "opacity-0 invisible pointer-events-none"}`}
      >
        <div className="flex h-[76px] items-center justify-between px-6">
          <span className="flex h-8 w-8 items-center justify-center rounded-full bg-white text-xs font-bold text-black">WT</span>
          <button
            type="button"
            aria-label="Close menu"
            onClick={() => setOpen(false)}
            className="flex h-9 w-9 items-center justify-center rounded-full bg-white text-black"
          >
            <span className="relative block h-4 w-4">
              <span className="absolute left-0 top-1/2 h-px w-4 -translate-y-1/2 rotate-45 bg-black" />
              <span className="absolute left-0 top-1/2 h-px w-4 -translate-y-1/2 -rotate-45 bg-black" />
            </span>
          </button>
        </div>

        <div className="flex flex-1 flex-col justify-center px-6 pb-12">
          {links.map((link, i) => (
            <a
              key={link.href}
              href={link.href}
              onClick={() => setOpen(false)}
              className={`border-b border-white/[0.06] py-6 font-display text-4xl font-bold tracking-tighter text-white transition-all duration-700 ease-[cubic-bezier(0.32,0.72,0,1)] hover:text-accent-bright ${open ? "translate-y-0 opacity-100" : "translate-y-12 opacity-0"}`}
              style={{ transitionDelay: `${100 + i * 80}ms` }}
            >
              <span className="mr-4 font-mono text-sm font-normal tracking-widest text-accent-bright">0{i + 1}</span>
              {link.label}
            </a>
          ))}

          <a
            href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
            target="_blank"
            rel="noopener noreferrer"
            aria-label="View source on GitHub"
            className={`mt-8 inline-flex h-12 w-full items-center justify-center rounded-full bg-white font-display text-sm font-bold text-black transition-all duration-700 ${open ? "translate-y-0 opacity-100 delay-500" : "translate-y-8 opacity-0"}`}
          >
            View source on GitHub — ↗
          </a>

          <p className={`mt-6 font-mono text-xs text-foreground-muted transition-all duration-700 ${open ? "opacity-100 delay-700" : "opacity-0"}`}>
            Windows 10 / 11 64-bit | Open Source | .NET 8 Native
          </p>
        </div>
      </div>
    </>
  );
}
