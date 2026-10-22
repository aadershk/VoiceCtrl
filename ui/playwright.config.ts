import { defineConfig } from "@playwright/test";

/**
 * Visual tests (spec §6): pages run in plain Chromium against the mock bridge, so they need no
 * Windows desktop and no WebView2. Baselines are per platform (Playwright's `-win32` suffix);
 * CI runs them on windows-latest so the local baselines are the CI baselines.
 */
export default defineConfig({
  testDir: "tests/visual",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  // About 1 Overlay snapshot in 100 differs by a few anti-aliased text pixels, with or without any
  // code change (measured in VED-49: 8 of 410 on the old stylesheet, 5 of 410 on the new). A real
  // regression fails every attempt, so CI retries twice instead of failing the PR on the noise.
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: "http://localhost:5173",
    browserName: "chromium",
    viewport: { width: 1700, height: 1000 },
    deviceScaleFactor: 1,
    // Reduced motion on (spec §6), so nothing is caught mid-animation.
    reducedMotion: "reduce",
    timezoneId: "Europe/Amsterdam",
    locale: "en-GB",
  },
  expect: {
    toHaveScreenshot: {
      animations: "disabled",
      caret: "hide",
      // Anti-aliasing differs slightly between GPUs and Windows builds; real regressions are larger.
      maxDiffPixelRatio: 0.002,
    },
  },
  webServer: {
    command: "npm run dev",
    url: "http://localhost:5173/gallery.html",
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
