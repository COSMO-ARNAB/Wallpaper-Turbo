import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Terms of Service",
  description: "Terms of Service and License for Wallpaper Turbo.",
};

export default function TermsPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border">
        <div className="mx-auto flex h-16 max-w-4xl items-center justify-between px-6">
          <Link
            href="/"
            className="flex items-center gap-2 font-semibold tracking-tight text-foreground transition-opacity hover:opacity-80"
          >
            <span className="flex h-7 w-7 items-center justify-center rounded-lg border border-border-bright bg-surface-raised font-mono text-xs font-bold text-accent-bright">
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
        <div className="tactile-card rounded-xl p-8 sm:p-10">
          <h1 className="text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
            Terms of Service
          </h1>
          <p className="mt-2 font-mono text-xs text-foreground-muted">
            Last Updated: January 1, 2025 · Open Source License
          </p>

          <div className="mt-8 space-y-6 text-sm leading-relaxed text-foreground-muted">
            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                1. Software Grant & License
              </h2>
              <p>
                Wallpaper Turbo is open-source software distributed under the terms of its repository license. You are free to download, use, modify, and redistribute the application in accordance with the project license published in the repository.
              </p>
            </section>

            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                2. Disclaimer of Warranties
              </h2>
              <p>
                The software is provided &quot;as is&quot;, without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and non-infringement.
              </p>
            </section>

            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                3. User Content & Media Responsibility
              </h2>
              <p>
                You are solely responsible for any video files, web assets, shaders, or media loaded into Wallpaper Turbo. Ensure you have appropriate rights and licenses for any copyrighted content displayed on your system.
              </p>
            </section>

            <section className="space-y-2">
              <h2 className="text-base font-semibold text-foreground">
                4. Source Code & Issue Reporting
              </h2>
              <p>
                Contributions, bug reports, and license terms can be reviewed on{" "}
                <a
                  href="https://github.com/COSMO-ARNAB/Wallpaper-Turbo"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="font-medium text-foreground underline underline-offset-2 hover:text-accent-bright"
                >
                  GitHub
                </a>
                .
              </p>
            </section>
          </div>
        </div>
      </main>
    </div>
  );
}
