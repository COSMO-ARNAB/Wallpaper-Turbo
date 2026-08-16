import type { MetadataRoute } from "next";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
    },
    sitemap: "https://cosmo-arnab.github.io/Wallpaper-Turbo/sitemap.xml",
  };
}
