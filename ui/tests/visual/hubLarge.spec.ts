import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// The Hub maximised or full screen (VED-50): content keeps a centred 1046 px column instead of
// stretching across the card. Sizes are the new default window, a 1080p and a 1440p monitor.
// Same mock host and fixed clock as hub.spec.ts.
const SIZES = [
  { width: 1350, height: 850 },
  { width: 1920, height: 1080 },
  { width: 2560, height: 1440 },
] as const;
const SCREENS = ["Dictations", "Dictionary", "Snippets", "Profiles", "Settings"] as const;
const CONTENT_CAP = 1046;

async function openScreen(page: Page, theme: string, screen: string): Promise<void> {
  await page.clock.setFixedTime(new Date("2026-09-29T09:50:00+02:00"));
  await page.goto(`/hub.html?theme=${theme}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await page.getByRole("navigation", { name: "Hub" }).getByRole("button", { name: screen }).click();
  await expect(page.locator(".hub-stage")).toHaveAttribute("data-screen", screen.toLowerCase());
}

for (const { width, height } of SIZES) {
  test.describe(`${width}x${height}`, () => {
    test.use({ viewport: { width, height } });

    for (const screen of SCREENS) {
      test(`${screen} content is a centred column no wider than ${CONTENT_CAP}px`, async ({ page }) => {
        await openScreen(page, "light", screen);
        const stage = await page.locator(".hub-stage").boundingBox();
        const card = await page.locator(".hub-scroll").boundingBox();
        expect(stage && card).toBeTruthy();
        expect(stage!.width).toBeLessThanOrEqual(CONTENT_CAP);
        // Centred in the card: equal space left and right of the column (within a pixel).
        const left = stage!.x - card!.x;
        const right = card!.x + card!.width - (stage!.x + stage!.width);
        expect(Math.abs(left - right)).toBeLessThanOrEqual(1);
      });
    }

    test("the Latest quote fills its card instead of stopping short", async ({ page }) => {
      await openScreen(page, "light", "Dictations");
      const hero = await page.locator(".vc-hero").boundingBox();
      const quote = await page.locator(".vc-hero-text").boundingBox();
      expect(hero && quote).toBeTruthy();
      // The card pads 30 px each side; a width cap on the text would leave more than that unused.
      expect(hero!.width - quote!.width).toBeLessThanOrEqual(61);
    });

    const themes = ["light", "dark"] as const;
    for (const theme of themes) {
      for (const screen of ["Dictations", "Settings"] as const) {
        test(`${screen}, ${theme}`, async ({ page }) => {
          await openScreen(page, theme, screen);
          await expect(page).toHaveScreenshot(`hub-large-${screen.toLowerCase()}-${width}-${theme}.png`);
        });
      }
    }
  });
}
