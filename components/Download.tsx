import { CopyButton } from "./CopyButton";
import { INSTALLER_FILENAME, type ReleaseInfo } from "@/lib/github";
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
  const version = release?.version ?? "v1.5.2";
  const filename = INSTALLER_FILENAME;
  const published = release?.publishedAt ? formatDate(release.publishedAt) : null;
  const checksum = release?.checksum;
  const releasesPage = release?.releaseUrl ?? "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases";

  return (
    <section id="download" className="border-t border-engraved bg-plate" aria-labelledby="download-heading">
      <div className="grid-shell py-16 lg:py-24">
        <Reveal>
          <h2 id="download-heading" className="font-display text-[clamp(2.5rem,5vw,4rem)] font-bold uppercase leading-[0.95] text-off-white">
            Take the installer.
          </h2>
          <p className="mt-4 max-w-[60ch] font-sans text-[17px] leading-[1.55] text-machine-grey">
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
            <span className="font-mono text-xs text-machine-grey">
              {filename} ({version}{published && `, ${published}`})
            </span>
          </div>
        </Reveal>

        <Reveal delay={160}>
          <div className="plate-frame mt-8 p-4">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <p className="font-mono text-xs text-machine-grey">{filename}</p>
                <p className="mt-1 font-mono text-xs">
                  <span className="rounded-[2px] border border-engraved bg-graphite px-2 py-0.5 text-amber">{version}</span>
                  {published && <span className="ml-2 text-machine-grey">{published}</span>}
                </p>
              </div>
              {checksum && (
                <div className="flex items-center gap-3">
                  <code className="max-w-[36ch] truncate font-mono text-xs text-off-white">{checksum}</code>
                  <CopyButton text={checksum} />
                </div>
              )}
            </div>
            {!checksum && (
              <p className="mt-3 font-mono text-xs text-machine-grey">
                Verify on <a href={releasesPage} target="_blank" rel="noopener noreferrer" className="text-amber underline">GitHub Releases</a>
              </p>
            )}
          </div>
        </Reveal>

        <div className="mt-8 grid grid-cols-1 gap-8 border-t border-engraved pt-8 md:grid-cols-2">
          <div>
            <h3 className="font-mono text-[11px] font-medium tracking-[0.12em] text-machine-grey">SYSTEM REQUIREMENTS</h3>
            <ul className="mt-3 space-y-1 font-sans text-sm text-off-white">
              <li>Windows 10 (Build 19041+) or Windows 11</li>
              <li>GPU with hardware video decoding</li>
            </ul>
          </div>
          <div>
            <h3 className="font-mono text-[11px] font-medium tracking-[0.12em] text-machine-grey">SOURCE</h3>
            <p className="mt-3 font-sans text-sm text-off-white">
              Source and releases on <a href={releasesPage} target="_blank" rel="noopener noreferrer" className="text-amber underline">GitHub</a>. 100% open source.
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}