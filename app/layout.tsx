import type { Metadata } from "next";
import { Big_Shoulders, Archivo, Spline_Sans_Mono } from "next/font/google";
import "./globals.css";

const displayFont = Big_Shoulders({
  variable: "--font-big-shoulders",
  subsets: ["latin"],
  display: "swap",
  weight: ["700", "800"],
  adjustFontFallback: false,
});

const sansFont = Archivo({
  variable: "--font-archivo",
  subsets: ["latin"],
  display: "swap",
  weight: ["400", "500", "600"],
});

const monoFont = Spline_Sans_Mono({
  variable: "--font-spline-mono",
  subsets: ["latin"],
  display: "swap",
  weight: ["400", "500", "600"],
});

export const metadata: Metadata = {
  metadataBase: new URL("https://cosmo-arnab.github.io/Wallpaper-Turbo"),
  title: {
    default: "Wallpaper Turbo · Lightweight Live Wallpapers for Windows",
    template: "%s | Wallpaper Turbo",
  },
  description:
    "Wallpaper Turbo is a lightweight, hardware-accelerated live wallpaper engine for Windows 10 and Windows 11.",
  alternates: {
    canonical: "/",
  },
  keywords: [
    "live wallpaper",
    "windows 10",
    "windows 11",
    "desktop wallpaper",
    "gpu accelerated",
    "hardware accelerated",
    "open source",
    "libvlc",
    "webview2",
  ],
  authors: [{ name: "Wallpaper Turbo Team" }],
  openGraph: {
    title: "Wallpaper Turbo · Lightweight Live Wallpapers for Windows",
    description:
      "Lightweight, hardware-accelerated live wallpaper engine for Windows 10 and Windows 11.",
    url: "https://cosmo-arnab.github.io/Wallpaper-Turbo",
    siteName: "Wallpaper Turbo",
    locale: "en_US",
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: "Wallpaper Turbo · Lightweight Live Wallpapers for Windows",
    description:
      "Lightweight, hardware-accelerated live wallpaper engine for Windows 10 and Windows 11.",
  },
  robots: {
    index: true,
    follow: true,
    googleBot: {
      index: true,
      follow: true,
      "max-video-preview": -1,
      "max-image-preview": "large",
      "max-snippet": -1,
    },
  },
  icons: {
    icon: "/Wallpaper-Turbo/icon.svg",
    apple: "/Wallpaper-Turbo/icon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className={`${displayFont.variable} ${sansFont.variable} ${monoFont.variable} power-on`} suppressHydrationWarning>
      <head>
        <script
          dangerouslySetInnerHTML={{
            __html: `try{const r=e=>e.removeAttribute('bis_skin_checked');document.querySelectorAll('[bis_skin_checked]').forEach(r);new MutationObserver(e=>{for(const t of e)if('bis_skin_checked'===t.attributeName)r(t.target)}).observe(document.documentElement,{attributes:true,subtree:true,attributeFilter:['bis_skin_checked']})}catch(e){}`,
          }}
        />
      </head>
      <body className="min-h-screen bg-graphite text-off-white antialiased" suppressHydrationWarning>
        {/* THESIS v3 — capability plate: the raw capability list is the design itself.
            OWN-WORLD: Graphite #101214 / Plate #16181B / Engraved #2A2E33 / Off-White #F2F0E9 / Machine Grey #6E7681 / Amber #FFB454, 2px radius, no blur/shadow/gradient
            RULE: amber means "this number was measured"; every frame is a nameplate; no AI imagery — empty feeds say NO SIGNAL */}
        {children}
      </body>
    </html>
  );
}
