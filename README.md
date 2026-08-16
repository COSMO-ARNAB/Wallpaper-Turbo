# Wallpaper Turbo Website

Official single-page site for [Wallpaper Turbo](https://github.com/COSMO-ARNAB/Wallpaper-Turbo) — a lightweight, hardware-accelerated live wallpaper engine for Windows.

## What is shipped

- Single-page Next.js (App Router) site with static export.
- Sections: Hero, Features, Download, Footer.
- Release version and SHA-256 checksum fetched server-side from GitHub Releases and cached with ISR (`revalidate: 3600`).
- Direct download link via GitHub’s `releases/latest/download/` alias.
- No hero video, no glass/blur, no separate docs/changelog routes, no webhook.

## Configure

GitHub coordinates are hard-coded in `lib/github.ts`:

```ts
export const GITHUB_OWNER = "COSMO-ARNAB";
export const GITHUB_REPO = "Wallpaper-Turbo";
export const INSTALLER_FILENAME = "Wallpaper_Turbo_Setup.exe";
```

Update these values if the repository or installer filename ever changes. The installer filename must remain fixed across releases for GitHub’s latest-release alias to work.

## Run locally

```bash
npm install
npm run dev
```

Open <http://localhost:3000>.

## Build

```bash
npm run build
```

The static export is written to `dist/`.

## Deploy to Vercel

1. Push this repository to GitHub.
2. Import the project on [Vercel](https://vercel.com).
3. Use the default Next.js settings.
4. Deploy.

No environment variables are required.

## Checksum source

The download section looks for the checksum in this order:

1. A release asset named `checksums.txt`.
2. The release body text (supports `SHA-256: <hash>` or `<hash>  filename` formats).

If neither is found, the page shows a graceful fallback linking to GitHub Releases.

## Notes

- This is the scoped-down MVP described in `design.md` and `architecture.md`.
- The `v2` redesign (hero video, liquid glass, separate routes) is intentionally deferred.
