import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

async function openGallery(page: Page): Promise<void> {
  await page.goto("/gallery.html");
  await page.evaluate(() => document.fonts.ready);
  // motion.ts lifts the first-paint guard two frames after the first paint.
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
}

for (const theme of ["light", "dark"] as const) {
  test(`gallery, ${theme}`, async ({ page }) => {
    await openGallery(page);
    await expect(page.getByTestId(`theme-${theme}`)).toHaveScreenshot(`gallery-${theme}.png`);
  });
}

test("the bundled faces load, with no fallback", async ({ page }) => {
  await openGallery(page);
  const loaded = await page.evaluate(() =>
    ["Sora", "Public Sans", "Newsreader"].map((family) => document.fonts.check(`16px "${family}"`)),
  );
  expect(loaded).toEqual([true, true, true]);
});
