import { CopyButton } from "./CopyButton";
import type { ReleaseInfo } from "@/lib/github";

interface DownloadProps {
  release: ReleaseInfo | null;
}

const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"
];

function formatDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return `${MONTHS[date.getUTCMonth()]} ${date.getUTCDate()}, ${date.getUTCFullYear()}`;
}

export function Download({ release }: DownloadProps) {
  const version = release?.version ?? "v1.0.0";
  const published = release?.publishedAt ? formatDate(release.publishedAt) : null;
  const checksum = release?.checksum;
  const releasesPage =
    release?.releaseUrl ??
    "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases";

  return (
    <section
      id="download"
      className="relative border-t border-border bg-background"
      aria-labelledby="download-heading"
    >
      <div className="relative mx-auto max-w-[1400px] px-6 py-24 lg:px-8">
        <div>
          <div className="tactile-card mx-auto max-w-3xl rounded-xl p-8 sm:p-10">
            <div className="flex flex-wrap items-center justify-between gap-4 border-b border-border pb-6">
              <div>
                <h2
                  id="download-heading"
                  className="text-2xl font-bold tracking-tight text-foreground sm:text-3xl"
                >
                  Download Wallpaper Turbo
                </h2>
                <p className="mt-1 text-sm text-foreground-muted">
                  Offline installer with automatic background updates.
                </p>
              </div>
              <div className="flex items-center gap-2">
                <span className="rounded border border-border-bright bg-surface-sunken px-2.5 py-1 font-mono text-xs font-semibold text-accent-bright">
                  {version}
                </span>
                <span className="rounded border border-border bg-surface-sunken px-2.5 py-1 font-mono text-xs text-foreground-muted">
                  x64 Native
                </span>
              </div>
            </div>

            <div className="mt-8 flex flex-col items-start justify-between gap-6 sm:flex-row sm:items-center">
              <div>
                <p className="text-base font-medium text-foreground">
                  Official Installer Package
                </p>
                <p className="mt-1 font-mono text-xs text-foreground-muted">
                  Windows 10 / 11 · 64-bit {published && `· Published ${published}`}
                </p>
              </div>

              <a
                href={release?.downloadUrl ?? "#"}
                className="inline-flex h-12 w-full items-center justify-center rounded-lg bg-accent px-8 text-sm font-bold text-accent-foreground shadow-sm transition-all hover:bg-accent/90 active:scale-[0.98] sm:w-auto"
              >
                <span>Download Setup.exe</span>
              </a>
            </div>

            {/* SHA-256 Checksum Container */}
            <div className="mt-8 rounded-xl border border-border bg-surface-sunken p-4">
              {checksum ? (
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="min-w-0 flex-1">
                    <p className="font-mono text-[10px] uppercase tracking-wider text-foreground-muted">
                      Cryptographic SHA-256 Checksum
                    </p>
                    <code className="mt-1 block truncate font-mono text-xs text-foreground">
                      {checksum}
                    </code>
                  </div>
                  <CopyButton text={checksum} />
                </div>
              ) : (
                <p className="text-xs text-foreground-muted">
                  Verify the latest cryptographic checksum directly on{" "}
                  <a
                    href={releasesPage}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="font-medium text-foreground underline underline-offset-2 hover:text-accent-bright"
                  >
                    GitHub Releases
                  </a>
                  .
                </p>
              )}
            </div>

            <div className="mt-10 grid grid-cols-1 gap-6 border-t border-border pt-8 sm:grid-cols-2">
              <div>
                <h3 className="text-xs font-semibold uppercase tracking-wider text-foreground-muted">
                  System Requirements
                </h3>
                <ul className="mt-2.5 space-y-1.5 text-sm text-foreground-muted">
                  <li className="flex items-center gap-2">
                    <span className="h-1 w-1 rounded-full bg-accent-bright" />
                    <span>Windows 10 (Build 19041+) or Windows 11</span>
                  </li>
                  <li className="flex items-center gap-2">
                    <span className="h-1 w-1 rounded-full bg-accent-bright" />
                    <span>GPU with hardware video decoding (DirectX 11)</span>
                  </li>
                </ul>
              </div>

              <div>
                <h3 className="text-xs font-semibold uppercase tracking-wider text-foreground-muted">
                  Open Source & Verification
                </h3>
                <p className="mt-2.5 text-sm leading-relaxed text-foreground-muted">
                  100% telemetry-free and offline-first. Review full source code and previous versions on{" "}
                  <a
                    href={releasesPage}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-foreground underline underline-offset-2 hover:text-accent-bright"
                  >
                    GitHub Releases
                  </a>
                  .
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
