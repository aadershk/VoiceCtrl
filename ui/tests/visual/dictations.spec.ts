import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// The Dictations screen (slice 17) at the Hub's default size, on the mock host; see
// src/hub/mockDictations.ts for the address fixtures. The clock is fixed at 09:50 on a Tuesday,
// so the sample Dictations read as they do on the approved board.
test.use({ viewport: { width: 1040, height: 680 } });

async function openDictations(page: Page, query: string): Promise<void> {
  await page.clock.setFixedTime(new Date("2026-09-29T09:50:00+02:00"));
  await page.goto(`/hub.html?${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Dictations");
  await page.waitForTimeout(300);
}

for (const theme of ["light", "dark"] as const) {
  test(`dictations, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}`);
    await expect(page).toHaveScreenshot(`dictations-${theme}.png`);
  });

  test(`dictations row hover, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}&failed=0`);
    await page.getByText(/load factor/).hover();
    await expect(page).toHaveScreenshot(`dictations-hover-${theme}.png`);
  });

  test(`dictations two failures and a retry, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}&failed=2&retrying=1`);
    await expect(page).toHaveScreenshot(`dictations-retrying-${theme}.png`);
  });

  // VED-76: a cancelled recording is kept, and its notice says "Recover text".
  test(`dictations cancelled recording, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}&cancelled=1`);
    await expect(page).toHaveScreenshot(`dictations-cancelled-${theme}.png`);
  });

  test(`dictations menu, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}`);
    await page.getByRole("button", { name: "More" }).click();
    await expect(page.getByRole("menu", { name: "Dictations" })).toBeVisible();
    await expect(page).toHaveScreenshot(`dictations-menu-${theme}.png`);
  });

  test(`dictations clear all, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}`);
    await page.getByRole("button", { name: "More" }).click();
    await page.getByRole("menuitem", { name: "Clear all Dictations" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await page.waitForTimeout(300);
    await expect(page).toHaveScreenshot(`dictations-clear-${theme}.png`);
  });

  test(`dictations search, no match, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}`);
    await page.getByRole("searchbox", { name: "Search Dictations" }).fill("invoice");
    await expect(page).toHaveScreenshot(`dictations-nomatch-${theme}.png`);
  });

  test(`dictations first run, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}&dictations=empty&failed=0`);
    await expect(page).toHaveScreenshot(`dictations-first-run-${theme}.png`);
  });

  test(`dictations not kept, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}&keep=none&failed=0`);
    await expect(page).toHaveScreenshot(`dictations-not-kept-${theme}.png`);
  });

  test(`dictations toast with undo, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}&failed=0`);
    await page.getByText(/load factor/).hover();
    await page.getByRole("region", { name: "Today" }).getByRole("button", { name: "Delete" }).first().click();
    await expect(page.getByRole("button", { name: "Undo" })).toBeVisible();
    await page.mouse.move(0, 0);
    await page.waitForTimeout(300);
    await expect(page).toHaveScreenshot(`dictations-deleted-${theme}.png`);
  });
}

// Smart cleanup kept the Original beside the newest two Dictations (ticket 37).
for (const theme of ["light", "dark"] as const) {
  test(`dictations with Restore original, ${theme}`, async ({ page }) => {
    await openDictations(page, `theme=${theme}&original=1&failed=0`);
    await expect(page).toHaveScreenshot(`dictations-original-${theme}.png`);
  });
}
