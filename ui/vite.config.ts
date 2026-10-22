import { resolve } from "node:path";
import react from "@vitejs/plugin-react";
import type { Plugin } from "vite";
import { defineConfig } from "vitest/config";

// Every surface is its own HTML entry (spec §6). Each surface slice adds its page here.
const pages = {
  hub: resolve(import.meta.dirname, "hub.html"),
  setup: resolve(import.meta.dirname, "setup.html"),
  overlay: resolve(import.meta.dirname, "overlay.html"),
  flyout: resolve(import.meta.dirname, "flyout.html"),
};

// The gallery is a dev tool: `npm run dev` serves it, the release build leaves it out.
const devPages = {
  gallery: resolve(import.meta.dirname, "gallery.html"),
};

/**
 * The page CSP (spec §3): nothing but the app's own files, and no network at all; the host does
 * all network work. Build only, because the Vite dev server needs inline scripts.
 */
function contentSecurityPolicy(): Plugin {
  const policy = "default-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
  return {
    name: "voicectrl-csp",
    apply: "build",
    transformIndexHtml: () => [
      { tag: "meta", attrs: { "http-equiv": "Content-Security-Policy", content: policy }, injectTo: "head-prepend" },
    ],
  };
}

export default defineConfig(({ command }) => ({
  plugins: [react(), contentSecurityPolicy()],
  // Relative asset URLs, so the build works from the host's virtual folder mapping.
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    // No data: URLs; the CSP allows only the app's own files.
    assetsInlineLimit: 0,
    rollupOptions: { input: command === "build" ? pages : { ...pages, ...devPages } },
  },
  server: { port: 5173, strictPort: true },
  test: {
    environment: "jsdom",
    include: ["tests/**/*.test.{ts,tsx}"],
    setupFiles: ["tests/setup.ts"],
    css: false,
  },
}));
