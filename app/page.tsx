import { Navbar } from "@/components/Navbar";
import { Hero } from "@/components/Hero";
import { Ticker } from "@/components/Ticker";
import { Features } from "@/components/Features";
import { Gallery } from "@/components/Gallery";
import { Engine } from "@/components/Engine";
import { Download } from "@/components/Download";
import { Footer } from "@/components/Footer";
import { getLatestRelease } from "@/lib/github";

export default async function Home() {
  const release = await getLatestRelease();

  return (
    <>
      <Navbar />
      <main id="main-content" suppressHydrationWarning>
        <Hero
          version={release?.version ?? null}
          releaseUrl={release?.releaseUrl}
        />
        <Ticker />
        <Features />
        <Gallery />
        <Engine />
        <Download release={release} />
      </main>
      <Footer />
    </>
  );
}
