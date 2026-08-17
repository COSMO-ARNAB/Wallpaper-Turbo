import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
  display: "swap",
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL("https://cosmo-arnab.github.io/Wallpaper-Turbo"),
  title: {
    default: "Wallpaper Turbo — Lightweight live wallpapers for Windows",
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
    title: "Wallpaper Turbo — Lightweight live wallpapers for Windows",
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
    title: "Wallpaper Turbo — Lightweight live wallpapers for Windows",
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
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable}`} suppressHydrationWarning>
      <body className="min-h-screen antialiased" suppressHydrationWarning>{children}</body>
    </html>
  );
}
