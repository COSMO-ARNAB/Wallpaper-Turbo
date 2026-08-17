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
      // Fallback silently: user can still highlight and copy the code text
    }
  };

  return (
    <button
      type="button"
      onClick={handleCopy}
      aria-label={copied ? "Checksum copied to clipboard" : "Copy checksum to clipboard"}
      aria-live="polite"
      className={`inline-flex h-9 min-w-[5rem] items-center justify-center gap-1.5 rounded-md border font-mono text-xs font-semibold transition-all active:scale-[0.97] ${
        copied
          ? "border-accent-bright bg-accent/20 text-accent-bright"
          : "border-border bg-surface text-foreground hover:border-border-bright hover:bg-surface-raised"
      }`}
    >
      {copied ? (
        <span>Copied</span>
      ) : (
        <span>Copy SHA</span>
      )}
    </button>
  );
}
