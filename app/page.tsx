import { Navbar } from "@/components/Navbar";
import { Hero } from "@/components/Hero";
import { Features } from "@/components/Features";
import { Download } from "@/components/Download";
import { Footer } from "@/components/Footer";
import { getLatestRelease } from "@/lib/github";

export default async function Home() {
  const release = await getLatestRelease();

  return (
    <>
      <Navbar />
      <main id="main-content">
        <Hero />
        <Features />
        <Download release={release} />
      </main>
      <Footer />
    </>
  );
}
