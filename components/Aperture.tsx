export function Aperture() {
  return (
    <section id="aperture" className="relative flex h-[40vh] items-center justify-center border-y border-[rgba(138,147,166,0.18)] bg-transparent" aria-hidden="true">
      <p className="font-mono text-xs text-backlight">
        This layer is z-index 0. Everything you have read sits on top of it.
      </p>
    </section>
  );
}
