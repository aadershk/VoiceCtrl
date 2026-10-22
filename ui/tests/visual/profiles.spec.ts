import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// Profiles (research 10) on the mock host, at the Hub's default size; see src/hub/mockProfiles.ts
// for the fixtures. Baselines are the screens the owner signed off, one per board state.
test.use({ viewport: { width: 1040, height: 680 } });

async function openProfiles(page: Page, query: string): Promise<void> {
  await page.goto(`/hub.html?${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await page.getByRole("navigation", { name: "Hub" }).getByRole("button", { name: "Profiles" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Profiles");
  // Moves the pointer off the sidebar, so no hover state is caught.
  await page.mouse.move(1000, 660);
}

for (const theme of ["light", "dark"] as const) {
  test(`profiles, ${theme}`, async ({ page }) => {
    await openProfiles(page, `theme=${theme}`);
    await expect(page.getByRole("heading", { name: "3 profiles" })).toBeVisible();
    await expect(page).toHaveScreenshot(`profiles-${theme}.png`);
  });

  test(`profiles editor, ${theme}`, async ({ page }) => {
    await openProfiles(page, `theme=${theme}`);
    await page.getByRole("button", { name: "Edit the Outlook profile" }).click();
    await expect(page.getByRole("button", { name: "Save profile" })).toBeVisible();
    await page.mouse.move(1000, 660);
    await expect(page).toHaveScreenshot(`profiles-editor-${theme}.png`);
  });

  test(`profiles add, app menu, ${theme}`, async ({ page }) => {
    await openProfiles(page, `theme=${theme}`);
    await page.getByRole("button", { name: "Add profile" }).click();
    await expect(page.getByRole("menu", { name: "Open apps" })).toBeVisible();
    await expect(page).toHaveScreenshot(`profiles-add-menu-${theme}.png`);
  });

  test(`profiles empty, ${theme}`, async ({ page }) => {
    await openProfiles(page, `theme=${theme}&profiles=empty`);
    await expect(page.getByRole("heading", { name: "No Profiles yet" })).toBeVisible();
    await expect(page).toHaveScreenshot(`profiles-empty-${theme}.png`);
  });
}
