"use client";

import { motion } from "motion/react";
import type { ReactNode } from "react";

interface RevealProps {
  children: ReactNode;
  className?: string;
  delay?: number;
}

// SSR-safe scroll reveal: the hidden pre-intersection state comes from
// Motion's inline styles; .reveal-force-visible pins the final state via CSS
// for users who prefer reduced motion, so server and client markup match.
export function Reveal({ children, className, delay = 0 }: RevealProps) {
  return (
    <motion.div
      className={`reveal-force-visible${className ? ` ${className}` : ""}`}
      initial={{ opacity: 0, y: 18 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: "-80px" }}
      transition={{ duration: 0.55, delay, ease: [0.21, 0.47, 0.32, 0.98] }}
    >
      {children}
    </motion.div>
  );
}
