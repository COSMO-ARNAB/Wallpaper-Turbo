import { Navbar } from "@/components/Navbar";
import { Hero } from "@/components/Hero";
import { Ticker } from "@/components/Ticker";
import { Features } from "@/components/Features";
import { Aperture } from "@/components/Aperture";
import { Gallery } from "@/components/Gallery";
import { Engine } from "@/components/Engine";
import { Download } from "@/components/Download";
import { Footer } from "@/components/Footer";
import { getLatestRelease } from "@/lib/github";

export default async function Home() {
  const release = await getLatestRelease();

  return (
    <>
      <Navbar version={release?.version ?? null} />
      <main id="main-content" className="pb-16 md:pb-20" suppressHydrationWarning>
        <Hero />
        <Ticker />
        <Features />
        <Aperture />
        <Gallery />
        <Engine />
        <Download release={release} />
      </main>
      <Footer />
    </>
  );
}
