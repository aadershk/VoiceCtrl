import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// The Hub frame at its default size (DESIGN §5) on the mock host; see src/hub/main.tsx for the
// address fixtures. Baselines are the frame the owner signed off, now around the real Dictations
// screen, so the clock is fixed as in dictations.spec.ts.
test.use({ viewport: { width: 1040, height: 680 } });

async function openHub(page: Page, query: string): Promise<void> {
  await page.clock.setFixedTime(new Date("2026-09-29T09:50:00+02:00"));
  await page.goto(`/hub.html?${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
}

for (const theme of ["light", "dark"] as const) {
  test(`hub frame, ${theme}`, async ({ page }) => {
    await openHub(page, `theme=${theme}`);
    await expect(page).toHaveScreenshot(`hub-${theme}.png`);
  });

  test(`hub Quick Find open, ${theme}`, async ({ page }) => {
    await openHub(page, `theme=${theme}`);
    await page.getByRole("combobox", { name: "Quick Find" }).fill("mo");
    await expect(page.getByRole("listbox")).toBeVisible();
    await expect(page).toHaveScreenshot(`hub-quickfind-${theme}.png`);
  });

  test(`hub status card warnings, ${theme}`, async ({ page }) => {
    await openHub(page, `theme=${theme}&status=noMicrophone`);
    await expect(page.locator(".vc-status")).toHaveScreenshot(`hub-status-nomic-${theme}.png`);
    await openHub(page, `theme=${theme}&status=paused`);
    await expect(page.locator(".vc-status")).toHaveScreenshot(`hub-status-paused-${theme}.png`);
  });

  test(`hub after an update, ${theme}`, async ({ page }) => {
    await openHub(page, `theme=${theme}&updated=4.1.0`);
    await expect(page.getByRole("region", { name: "Updated to 4.1.0" })).toBeVisible();
    await expect(page).toHaveScreenshot(`hub-updated-${theme}.png`);
  });
}
