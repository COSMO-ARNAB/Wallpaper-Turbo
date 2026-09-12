import { Navbar } from "@/components/Navbar";
import { Hero } from "@/components/Hero";
import { Ticker } from "@/components/Ticker";
import { Engine } from "@/components/Engine";
import { Features } from "@/components/Features";
import { Download } from "@/components/Download";
import { Footer } from "@/components/Footer";
import { getLatestRelease } from "@/lib/github";

export default async function Home() {
  const release = await getLatestRelease();

  return (
    <>
      <Navbar version={release?.version ?? null} />
      <main id="main-content" suppressHydrationWarning>
        <Hero />
        <Ticker />
        <Engine />
        <Features />
        <Download release={release} />
      </main>
      <Footer />
    </>
  );
}
