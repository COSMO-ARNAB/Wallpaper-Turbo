import type { Metadata } from "next";
import { Big_Shoulders, Archivo, Spline_Sans_Mono } from "next/font/google";
import "./globals.css";
import { OSD } from "@/components/OSD";

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
    images: [
      {
        url: "/screenshot.png",
        width: 1920,
        height: 1080,
        alt: "Wallpaper Turbo Windows Application",
      },
    ],
    locale: "en_US",
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: "Wallpaper Turbo · Lightweight Live Wallpapers for Windows",
    description:
      "Lightweight, hardware-accelerated live wallpaper engine for Windows 10 and Windows 11.",
    images: ["/screenshot.png"],
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
    icon: "/icon.svg",
    apple: "/icon.svg",
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
      <body className="min-h-screen bg-panel-off text-backlight antialiased" suppressHydrationWarning>
        {/* THESIS: page is a display, wallpaper layer fixed at z-0 beneath all content, page demonstrates disappearing by disappearing itself
            OWN-WORLD: Panel Off #05070D / Black Level #0C1220 / Backlight #E9EDF3 / Standby #8A93A6 / Signal #60CDFF / Tungsten #FFC98B, hairlines 18% Standby, 2px radius, Big Shoulders 700-800 + Archivo 400-600 + Spline Sans Mono tabular-nums, no blur/shadow/gradient
            STORY: visitor sees wallpaper under icons in hero, understands it renders under SHELLDLL_DefView, believes zero FPS tax via live GPU readout, downloads from left-aligned block
            FIRST VIEWPORT: full viewport wallpaper layer fixed z-0, left-anchored Big Shoulders H1 Live wallpapers Set one It renders under your icons, two CTAs Download for Windows + See how it works, bottom-left real icons + layer readout icons > SHELLDLL_DefView > wallpaper layer, nav 56px Panel Off hairline, OSD fixed bottom-left
            FORM: left-anchored 12-col 1440 clamp(24px,5vw,120px) hairline-separated rows, wallpapers bleed edge-to-edge, OSD signature, seed v2-display-field
            FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, and DESIGN.md */}
        <div aria-hidden="true" className="wallpaper-layer">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/gallery-aurora.png"
            alt=""
            className="h-full w-full object-cover"
            style={{ opacity: 0.9 }}
          />
        </div>
        <div className="relative z-10">
          {children}
        </div>
        <OSD />
      </body>
    </html>
  );
}
