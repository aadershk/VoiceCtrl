import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// Style (ticket 56) on the mock host, at the Hub's default size; see src/hub/mockStyle.ts for the
// fixtures. Baselines are the screens the owner signed off.
test.use({ viewport: { width: 1040, height: 680 } });

async function openStyle(page: Page, query: string): Promise<void> {
  await page.goto(`/hub.html?${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await page.getByRole("navigation", { name: "Hub" }).getByRole("button", { name: "Style" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Style");
  await expect(page.getByRole("region", { name: "Personal chat" })).toBeVisible();
  // Moves the pointer off the sidebar, so no hover state is caught.
  await page.mouse.move(1000, 660);
}

for (const theme of ["light", "dark"] as const) {
  test(`style, ${theme}`, async ({ page }) => {
    await openStyle(page, `theme=${theme}`);
    await expect(page).toHaveScreenshot(`style-${theme}.png`);
  });

  test(`style, whole page with moved apps, ${theme}`, async ({ page }) => {
    await page.setViewportSize({ width: 1040, height: 1480 });
    await openStyle(page, `theme=${theme}&style=moved`);
    await expect(page).toHaveScreenshot(`style-moved-${theme}.png`);
  });

  test(`style, move menu, ${theme}`, async ({ page }) => {
    await openStyle(page, `theme=${theme}`);
    await page.getByRole("region", { name: "Personal chat" }).getByRole("button", { name: /^Discord:/ }).click();
    await expect(page.getByRole("menu", { name: "Move Discord to" })).toBeVisible();
    await expect(page).toHaveScreenshot(`style-move-menu-${theme}.png`);
  });
}
