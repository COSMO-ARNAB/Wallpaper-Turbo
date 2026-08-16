"use client";

import { useState } from "react";

interface CopyButtonProps {
  text: string;
}

export function CopyButton({ text }: CopyButtonProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Fail silently — the checksum is still visible and selectable.
    }
  };

  return (
    <button
      type="button"
      onClick={handleCopy}
      aria-label={copied ? "Checksum copied" : "Copy checksum to clipboard"}
      aria-live="polite"
      className="inline-flex h-11 min-w-[4.5rem] items-center justify-center rounded-md border border-border bg-surface px-3 text-sm font-medium text-foreground transition-colors hover:bg-foreground/5 active:scale-[0.98]"
    >
      {copied ? "Copied" : "Copy"}
    </button>
  );
}
