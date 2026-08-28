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
  const version = release?.version ?? "v1.5.1";
  const published = release?.publishedAt ? formatDate(release.publishedAt) : null;
  const checksum = release?.checksum;
  const releasesPage = release?.releaseUrl ?? "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases";

  return (
    <section id="download" className="bg-panel-off" aria-labelledby="download-heading">
      <div className="grid-shell py-16">
        <Reveal>
          <h2 id="download-heading" className="font-display text-[clamp(2.5rem,5vw,4rem)] font-bold uppercase text-backlight">
            Download Wallpaper Turbo.
          </h2>
          <p className="mt-4 max-w-[60ch] font-sans text-sm leading-relaxed text-standby">
            One installer. Windows 10/11, 64-bit. No telemetry, no account.
          </p>
        </Reveal>

        <Reveal delay={100}>
          <div className="mt-8 flex flex-wrap items-center gap-3">
            <a
              href={release?.downloadUrl ?? "#"}
              className="btn-primary inline-flex h-10 items-center justify-center px-6 font-sans text-[15px] font-semibold"
            >
              Download for Windows
            </a>
            <span className="font-mono text-xs text-standby">
              WallpaperTurbo-1.4.0.exe · {version} {published && `· ${published}`}
            </span>
          </div>
        </Reveal>

        <Reveal delay={160}>
          <div className="mt-8 border border-[rgba(138,147,166,0.18)] bg-black-level p-4">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <p className="font-mono text-xs text-standby">WallpaperTurbo-1.4.0.exe</p>
                <p className="mt-1 font-mono text-xs">
                  <span className="border border-[rgba(138,147,166,0.18)] bg-panel-off px-2 py-0.5 text-signal">{version}</span>
                  {published && <span className="ml-2 text-standby">{published}</span>}
                </p>
              </div>
              {checksum && (
                <div className="flex items-center gap-3">
                  <code className="max-w-[36ch] truncate font-mono text-xs text-backlight">{checksum}</code>
                  <CopyButton text={checksum} />
                </div>
              )}
            </div>
            {!checksum && (
              <p className="mt-3 font-mono text-xs text-standby">
                Verify on <a href={releasesPage} target="_blank" rel="noopener noreferrer" className="text-signal underline">GitHub Releases</a>
              </p>
            )}
          </div>
        </Reveal>

        <div className="mt-8 grid grid-cols-1 gap-8 border-t border-[rgba(138,147,166,0.18)] pt-8 md:grid-cols-2">
          <div>
            <h3 className="font-mono text-xs font-medium text-standby">System requirements</h3>
            <ul className="mt-3 space-y-1 font-sans text-sm text-standby">
              <li>Windows 10 (Build 19041+) or Windows 11</li>
              <li>GPU with hardware video decoding</li>
            </ul>
          </div>
          <div>
            <h3 className="font-mono text-xs font-medium text-standby">GitHub</h3>
            <p className="mt-3 font-sans text-sm text-standby">
              Source and releases on <a href={releasesPage} target="_blank" rel="noopener noreferrer" className="text-signal underline">GitHub</a>. 100% open source.
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}
