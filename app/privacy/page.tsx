import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Privacy Policy",
  description: "Privacy Policy for Wallpaper Turbo. 100% offline, zero data collection.",
};

export default function PrivacyPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border">
        <div className="mx-auto flex h-16 max-w-4xl items-center justify-between px-6">
          <Link
            href="/"
            className="flex items-center gap-2 font-semibold tracking-tight text-foreground transition-opacity hover:opacity-80"
          >
            <span className="flex h-7 w-7 items-center justify-center rounded-[2px] border border-engraved bg-plate font-mono text-xs font-bold text-amber">
              WT
            </span>
            <span>Wallpaper Turbo</span>
          </Link>
          <Link
            href="/"
            className="font-mono text-xs text-foreground-muted hover:text-foreground"
          >
            ← Back to Home
          </Link>
        </div>
      </header>

      <main className="mx-auto max-w-4xl px-6 py-16">
        <div className="plate-frame p-8 sm:p-10">
          <h1 className="font-display text-3xl font-bold uppercase tracking-tight text-off-white sm:text-4xl">
            Privacy Policy
          </h1>
          <p className="mt-2 font-mono text-xs text-foreground-muted">
            Effective Date: January 1, 2025 · Offline-First Architecture
          </p>

          <div className="mt-8 space-y-6 text-sm leading-relaxed text-foreground-muted">
            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                1. Zero Data Collection & Zero Telemetry
              </h2>
              <p>
                Wallpaper Turbo is designed as a standalone, offline desktop utility for Windows. We do not collect, store, transmit, or sell any personal information, telemetry, usage statistics, diagnostic reports, or file metadata.
              </p>
            </section>

            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                2. Local File & Wallpaper Processing
              </h2>
              <p>
                All video decoding (via LibVLC) and HTML5/WebGL rendering (via WebView2) execute entirely on your local machine using your hardware. Your wallpaper media, playlist configurations, and settings never leave your computer.
              </p>
            </section>

            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                3. Network Activity (Updates Only)
              </h2>
              <p>
                The optional Auto-Updater connects strictly to GitHub Releases API (<code className="rounded-[2px] bg-graphite px-1.5 py-0.5 font-mono text-xs text-off-white">api.github.com</code>) to check for the latest version tag and verify cryptographic SHA-256 signatures. No user identifiers or machine IDs are transmitted.
              </p>
            </section>

            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                4. Open Source Transparency
              </h2>
              <p>
                The complete source code for Wallpaper Turbo is publicly auditable on{" "}
                <a
                  href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="font-medium text-off-white underline underline-offset-2 hover:text-amber"
                >
                  GitHub
                </a>
                . Anyone can inspect and verify our network calls and privacy architecture.
              </p>
            </section>
          </div>
        </div>
      </main>
    </div>
  );
}
