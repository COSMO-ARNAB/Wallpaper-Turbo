import type { Metadata } from "next";
import { Plus_Jakarta_Sans, JetBrains_Mono, Space_Grotesk } from "next/font/google";
import "./globals.css";

const sansFont = Plus_Jakarta_Sans({
  variable: "--font-sans-main",
  subsets: ["latin"],
  display: "swap",
});

const displayFont = Space_Grotesk({
  variable: "--font-display-main",
  subsets: ["latin"],
  display: "swap",
});

const monoFont = JetBrains_Mono({
  variable: "--font-mono-main",
  subsets: ["latin"],
  display: "swap",
  preload: false,
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
    <html lang="en" className={`${sansFont.variable} ${displayFont.variable} ${monoFont.variable}`} suppressHydrationWarning>
      <head>
        <script
          dangerouslySetInnerHTML={{
            __html: `try{const r=e=>e.removeAttribute('bis_skin_checked');document.querySelectorAll('[bis_skin_checked]').forEach(r);new MutationObserver(e=>{for(const t of e)if('bis_skin_checked'===t.attributeName)r(t.target)}).observe(document.documentElement,{attributes:true,subtree:true,attributeFilter:['bis_skin_checked']})}catch(e){}`,
          }}
        />
      </head>
      <body className="min-h-screen antialiased bg-background text-foreground" suppressHydrationWarning>
        <div aria-hidden="true" className="grain-overlay" />
        {children}
      </body>
    </html>
  );
}
