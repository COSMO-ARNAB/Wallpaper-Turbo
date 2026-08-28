"use client";

import { useEffect, useState } from "react";

export function OSD() {
  const [section, setSection] = useState("hero");
  const [res, setRes] = useState("—");
  const [hz, setHz] = useState("—");
  const [layer, setLayer] = useState<"running" | "suspended">("running");

  useEffect(() => {
    const updateRes = () => setRes(`${window.innerWidth}×${window.innerHeight}`);
    updateRes();
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    window.addEventListener("resize", updateRes);
    return () => window.removeEventListener("resize", updateRes);
  }, []);

  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    let frames = 0;
    let start = performance.now();
    let raf = 0;
    const measure = (now: number) => {
      frames++;
      if (now - start < 1000) {
        raf = requestAnimationFrame(measure);
      } else {
        const fps = Math.round((frames * 1000) / (now - start));
        const hzVal = fps >= 59 && fps <= 61 ? 60 : fps >= 143 && fps <= 147 ? 144 : fps >= 239 && fps <= 242 ? 240 : fps;
        setHz(String(hzVal));
      }
    };
    raf = requestAnimationFrame(measure);
    return () => cancelAnimationFrame(raf);
  }, []);

  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const sections = ["hero", "features", "gallery", "engine", "download"];
    const obs = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((e) => e.isIntersecting)
          .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
        if (visible?.target.id) setSection(visible.target.id);
      },
      { threshold: [0.1, 0.5], rootMargin: "-20% 0px -60% 0px" }
    );
    sections.forEach((id) => {
      const el = document.getElementById(id);
      if (el) obs.observe(el);
    });
    return () => obs.disconnect();
  }, []);

  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const onScroll = () => {
      const hero = document.getElementById("hero");
      const aperture = document.getElementById("aperture");
      const heroRect = hero?.getBoundingClientRect();
      const apRect = aperture?.getBoundingClientRect();
      const visible = (heroRect && heroRect.bottom > 100 && heroRect.top < window.innerHeight) || (apRect && apRect.top < window.innerHeight && apRect.bottom > 100);
      setLayer(visible ? "running" : "suspended");
    };
    let t: number | null = null;
    const throttled = () => {
      if (t) return;
      t = window.setTimeout(() => {
        onScroll();
        t = null;
      }, 500) as unknown as number;
    };
    window.addEventListener("scroll", throttled, { passive: true });
    onScroll();
    return () => window.removeEventListener("scroll", throttled);
  }, []);

  return (
    <div
      className="osd"
      role="group"
      aria-label="Display readout"
      tabIndex={0}
    >
      <div className="hidden md:block space-y-0.5">
        <div className="flex justify-between gap-6"><span className="text-standby">input</span><span>{section}</span></div>
        <div className="flex justify-between gap-6"><span className="text-standby">res</span><span>{res}</span></div>
        <div className="flex justify-between gap-6"><span className="text-standby">hz</span><span>{hz}</span></div>
        <div className="flex justify-between gap-6 items-center">
          <span className="text-standby">layer</span>
          <span className="flex items-center gap-1.5" aria-live="polite">
            <span className={`h-1.5 w-1.5 rounded-full ${layer === "running" ? "bg-tungsten" : "bg-standby"}`} aria-hidden="true" />
            {layer}
          </span>
        </div>
      </div>
      <div className="md:hidden font-mono text-xs">
        {res} · {hz === "—" ? "—" : `${hz} Hz`}
      </div>
    </div>
  );
}
