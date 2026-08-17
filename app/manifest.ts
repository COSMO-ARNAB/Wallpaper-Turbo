import type { MetadataRoute } from "next";

export const dynamic = "force-static";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Wallpaper Turbo",
    short_name: "WallpaperTurbo",
    description:
      "Lightweight, hardware-accelerated live wallpapers for Windows 10 and Windows 11.",
    start_url: "/",
    display: "standalone",
    background_color: "#0c0c0e",
    theme_color: "#0c0c0e",
    icons: [
      {
        src: "/icon.svg",
        sizes: "any",
        type: "image/svg+xml",
      },
    ],
  };
}
