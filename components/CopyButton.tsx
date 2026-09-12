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
      aria-label={copied ? "SHA-256 checksum copied to clipboard" : "Copy SHA-256 checksum to clipboard"}
      aria-live="polite"
      className={`inline-flex h-9 min-w-[5rem] items-center justify-center gap-1.5 rounded-[2px] border font-mono text-xs font-semibold transition-colors ${
        copied
          ? "border-amber bg-plate text-amber"
          : "border-engraved bg-graphite text-machine-grey hover:bg-plate hover:text-off-white"
      }`}
    >
      {copied ? (
        <span>Copied</span>
      ) : (
        <span>Copy SHA-256</span>
      )}
    </button>
  );
}