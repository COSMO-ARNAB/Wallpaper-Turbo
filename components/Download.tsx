import { CopyButton } from "./CopyButton";
import type { ReleaseInfo } from "@/lib/github";
import { Reveal } from "./Reveal";

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
      className="relative border-t border-border bg-background py-24"
      aria-labelledby="download-heading"
    >
      <div aria-hidden="true" className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_at_center,rgba(56,189,248,0.06),transparent_60%)]" />

      <div className="relative mx-auto max-w-[1400px] px-6 lg:px-8">
        <Reveal>
          <div className="mx-auto max-w-3xl text-center">
            <div className="eyebrow mx-auto">Download · SHA-256 verified</div>
            <h2
              id="download-heading"
              className="mt-4 font-display text-4xl font-bold tracking-tighter text-foreground sm:text-5xl"
            >
              Download Wallpaper Turbo.
            </h2>
            <p className="mx-auto mt-3 max-w-xl text-sm leading-relaxed text-foreground-muted">
              Offline installer with automatic background updates. One executable, hardware-accelerated, telemetry-free.
            </p>
          </div>
        </Reveal>

        <Reveal delay={100}>
          <div className="bezel-shell mx-auto mt-10 max-w-3xl">
            <div className="bezel-core p-6 sm:p-8">
              <div className="flex flex-wrap items-center justify-between gap-4 border-b border-border pb-6">
                <div>
                  <h3 className="font-display text-lg font-semibold tracking-tight text-foreground">Official Installer Package</h3>
                  <p className="mt-1 font-mono text-xs text-foreground-muted">
                    Windows 10 / 11 64-bit {published && `· Published ${published}`}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <span className="rounded-full border border-accent-bright/30 bg-accent/15 px-3 py-1 font-mono text-xs font-semibold text-accent-bright">
                    {version}
                  </span>
                  <span className="rounded-full border border-border bg-surface-sunken px-3 py-1 font-mono text-xs text-foreground-muted">
                    x64 Native
                  </span>
                </div>
              </div>

              <div className="mt-6 flex flex-col items-start justify-between gap-6 sm:flex-row sm:items-center">
                <div>
                  <p className="font-display text-sm font-semibold text-foreground">Wallpaper_Turbo_Setup.exe</p>
                  <p className="mt-1 font-mono text-xs text-foreground-muted">Direct download via GitHub Releases — no telemetry</p>
                </div>

                <a
                  href={release?.downloadUrl ?? "#"}
                  className="group inline-flex h-11 w-full items-center justify-between gap-3 rounded-full bg-white pl-6 pr-2 font-display text-sm font-semibold text-black transition-all duration-300 hover:bg-white/90 active:scale-[0.98] sm:w-auto"
                >
                  <span>Download Wallpaper Turbo</span>
                  <span className="flex h-7 w-7 items-center justify-center rounded-full bg-black text-white transition-transform duration-300 group-hover:translate-x-0.5 group-hover:-translate-y-0.5">
                    ↗
                  </span>
                </a>
              </div>

              {/* SHA-256 */}
              <div className="mt-6 rounded-2xl border border-border bg-surface-sunken p-4">
                {checksum ? (
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="min-w-0 flex-1">
                      <p className="font-mono text-[10px] uppercase tracking-widest text-foreground-muted">
                        SHA-256 checksum
                      </p>
                      <code className="mt-1 block truncate font-mono text-xs text-foreground">{checksum}</code>
                    </div>
                    <CopyButton text={checksum} />
                  </div>
                ) : (
                  <p className="text-xs text-foreground-muted">
                    Verify the checksum on{" "}
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

              <div className="mt-8 grid grid-cols-1 gap-6 border-t border-border pt-6 sm:grid-cols-2">
                <div>
                  <h4 className="font-mono text-[10px] uppercase tracking-widest text-foreground-muted">System Requirements</h4>
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
                  <h4 className="font-mono text-[10px] uppercase tracking-widest text-foreground-muted">Open Source & Verification</h4>
                  <p className="mt-2.5 text-sm leading-relaxed text-foreground-muted">
                    100% telemetry-free and offline-first. Review source and previous versions on{" "}
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
        </Reveal>
      </div>
    </section>
  );
}
