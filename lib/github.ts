export const GITHUB_OWNER = "COSMO-ARNAB";
export const GITHUB_REPO = "Wallpaper-Turbo";
export const INSTALLER_FILENAME = "Wallpaper_Turbo_Setup.exe";

export interface ReleaseInfo {
  version: string;
  publishedAt: string;
  checksum: string | null;
  downloadUrl: string;
  releaseUrl: string;
}

interface GitHubAsset {
  name: string;
  browser_download_url: string;
  digest?: string;
}

interface GitHubRelease {
  tag_name: string;
  published_at: string;
  body: string | null;
  html_url: string;
  assets: GitHubAsset[];
}

const LATEST_RELEASE_URL = `https://api.github.com/repos/${GITHUB_OWNER}/${GITHUB_REPO}/releases/latest`;
const DOWNLOAD_URL = `https://github.com/${GITHUB_OWNER}/${GITHUB_REPO}/releases/latest/download/${INSTALLER_FILENAME}`;
const RELEASES_PAGE_URL = `https://github.com/${GITHUB_OWNER}/${GITHUB_REPO}/releases`;

const FALLBACK_RELEASE: ReleaseInfo = {
  version: "v1.5.1",
  publishedAt: "2026-08-06T17:26:55Z",
  checksum: "076d567392c98c5bd9df30ee3b3d0c8b104975db5adcd464da37f4e8279c4d2c",
  downloadUrl: DOWNLOAD_URL,
  releaseUrl: RELEASES_PAGE_URL,
};

function extractSha256(text: string, filename: string): string | null {
  const escaped = filename.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

  const paired = new RegExp(
    `^([a-fA-F0-9]{64})\\s+[*]?${escaped}$`,
    "im"
  ).exec(text);
  if (paired) return paired[1].toLowerCase();

  const labeled = /SHA-?256\s*[:=]\s*([a-fA-F0-9]{64})/i.exec(text);
  if (labeled) return labeled[1].toLowerCase();

  const standalone = /^([a-fA-F0-9]{64})\s*$/m.exec(text);
  if (standalone) return standalone[1].toLowerCase();

  return null;
}

async function fetchChecksumsText(assets: GitHubAsset[]): Promise<string | null> {
  try {
    const checksumAsset = assets.find(
      (asset) => asset.name.toLowerCase() === "checksums.txt"
    );
    if (!checksumAsset) return null;

    const res = await fetch(checksumAsset.browser_download_url, {
      headers: { "User-Agent": "Wallpaper-Turbo-Website" },
      next: { revalidate: 3600 },
    });

    if (!res.ok) return null;
    return await res.text();
  } catch {
    return null;
  }
}

function extractDigestChecksum(assets: GitHubAsset[]): string | null {
  const installerAsset = assets.find(
    (asset) => asset.name === INSTALLER_FILENAME
  );
  if (!installerAsset?.digest) return null;

  const match = /^sha256:([a-fA-F0-9]{64})$/i.exec(installerAsset.digest);
  return match ? match[1].toLowerCase() : null;
}

export async function getLatestRelease(): Promise<ReleaseInfo> {
  try {
    const res = await fetch(LATEST_RELEASE_URL, {
      headers: {
        "User-Agent": "Wallpaper-Turbo-Website",
        Accept: "application/vnd.github.v3+json",
      },
      next: { revalidate: 3600 },
    });

    if (!res.ok) {
      return FALLBACK_RELEASE;
    }

    const release: GitHubRelease = await res.json();

    let checksum: string | null = null;

    const checksumsText = await fetchChecksumsText(release.assets);
    if (checksumsText) {
      checksum = extractSha256(checksumsText, INSTALLER_FILENAME);
    }

    if (!checksum && release.body) {
      checksum = extractSha256(release.body, INSTALLER_FILENAME);
    }

    if (!checksum) {
      checksum = extractDigestChecksum(release.assets);
    }

    return {
      version: release.tag_name,
      publishedAt: release.published_at,
      checksum: checksum ?? FALLBACK_RELEASE.checksum,
      downloadUrl: DOWNLOAD_URL,
      releaseUrl: RELEASES_PAGE_URL,
    };
  } catch {
    return FALLBACK_RELEASE;
  }
}
