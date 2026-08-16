import { CopyButton } from "./CopyButton";
import type { ReleaseInfo } from "@/lib/github";

interface DownloadProps {
  release: ReleaseInfo | null;
}

function formatDate(iso: string): string {
  const date = new Date(iso);
  return date.toLocaleDateString("en-US", {
    year: "numeric",
    month: "long",
    day: "numeric",
  });
}

export function Download({ release }: DownloadProps) {
  const version = release?.version ?? "v?.?.?";
  const published = release?.publishedAt ? formatDate(release.publishedAt) : null;
  const checksum = release?.checksum;

  return (
    <section
      id="download"
      className="border-y border-border bg-surface"
      aria-labelledby="download-heading"
    >
      <div className="mx-auto max-w-[1400px] px-6 py-24 lg:px-8">
        <div className="max-w-2xl">
          <h2
            id="download-heading"
            className="text-3xl font-semibold tracking-tight text-foreground sm:text-4xl"
          >
            Download Wallpaper Turbo
          </h2>

          <p className="mt-4 font-mono text-sm text-foreground-muted">
            {version} {published && `· ${published}`} · Windows 10/11 · x64
          </p>

          <a
            href={release?.downloadUrl ?? "#"}
            className="mt-8 inline-flex h-12 min-w-[16rem] items-center justify-center rounded-md bg-accent px-8 text-base font-semibold text-accent-foreground transition-colors hover:bg-accent/90 active:scale-[0.98]"
          >
            Download Wallpaper_Turbo_Setup.exe
          </a>

          <div className="mt-8">
            {checksum ? (
              <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                <div className="min-w-0">
                  <p className="text-xs font-medium uppercase tracking-wide text-foreground-muted">
                    SHA-256
                  </p>
                  <code className="mt-1 block break-all font-mono text-sm text-foreground">
                    {checksum}
                  </code>
                </div>
                <CopyButton text={checksum} />
              </div>
            ) : (
              <p className="text-sm text-foreground-muted">
                Checksum unavailable right now. Verify the file on{" "}
                <a
                  href={release?.releaseUrl ?? "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases"}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-foreground underline underline-offset-2 hover:text-accent-foreground"
                >
                  GitHub Releases
                </a>
                .
              </p>
            )}
          </div>

          <div className="mt-10 border-t border-border pt-8">
            <h3 className="text-sm font-semibold text-foreground">
              System requirements
            </h3>
            <ul className="mt-3 space-y-1 text-sm text-foreground-muted">
              <li>Windows 10 or Windows 11 (64-bit)</li>
              <li>A GPU with hardware video decoding support recommended</li>
            </ul>
          </div>

          <p className="mt-8 text-sm text-foreground-muted">
            Looking for an older version?{" "}
            <a
              href={release?.releaseUrl ?? "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases"}
              target="_blank"
              rel="noreferrer"
              className="text-foreground underline underline-offset-2 hover:text-accent-foreground"
            >
              Previous releases on GitHub
            </a>
            .
          </p>
        </div>
      </div>
    </section>
  );
}
