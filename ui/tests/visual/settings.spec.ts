import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// The Settings page's Dictation group (slice 20) at the Hub's default size, on the mock host; see
// src/hub/mockSettings.ts for the address fixtures. The level is held still for the screenshot.
test.use({ viewport: { width: 1040, height: 680 } });

async function openSettings(page: Page, query: string, micControl: RegExp | string = /^Microphone:/): Promise<void> {
  await page.goto(`/hub.html?level=0.55&${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await page.getByRole("navigation", { name: "Hub" }).getByRole("button", { name: "Settings" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Settings");
  await expect(page.getByRole("button", { name: micControl })).toBeVisible();
  // Let the screen change (DESIGN §10) and the level bars' short transition finish.
  await page.waitForTimeout(600);
}

const dictation = (page: Page) => page.getByRole("region", { name: "Dictation" });

for (const theme of ["light", "dark"] as const) {
  test(`settings dictation, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&trigger=F9`);
    await expect(page).toHaveScreenshot(`settings-${theme}.png`);
  });

  // VED-70: the shipped defaults, Left Ctrl + Left Win to talk and Left Ctrl + Left Shift to rewrite.
  test(`settings dictation defaults, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&talk=1`);
    await expect(page).toHaveScreenshot(`settings-defaults-${theme}.png`);
  });

  // VED-74: with several keyboard layouts, the Rewrite default says it also switches the language.
  test(`settings hotkey conflict, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&talk=1&languages=many`);
    await expect(dictation(page).getByText(/also switches your keyboard language/)).toBeVisible();
    await expect(dictation(page)).toHaveScreenshot(`settings-conflict-${theme}.png`);
  });

  test(`settings microphone menu, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}`);
    await page.getByRole("button", { name: /^Microphone:/ }).click();
    await expect(page.getByRole("menu", { name: "Microphone" })).toBeVisible();
    await page.waitForTimeout(400);
    await expect(page).toHaveScreenshot(`settings-mic-menu-${theme}.png`);
  });

  test(`settings press a key with a hint, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}`);
    await page.getByRole("button", { name: "Change Hotkey" }).click();
    await page.keyboard.press("a");
    await expect(dictation(page).getByText(/Choose a key you don't type with/)).toBeVisible();
    await expect(dictation(page)).toHaveScreenshot(`settings-capture-${theme}.png`);
  });

  test(`settings notes, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&mics=unplugged&pasteTaken=1`);
    await expect(dictation(page)).toHaveScreenshot(`settings-notes-${theme}.png`);
  });

  test(`settings no microphone, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&mics=none&status=noMicrophone`, "No microphone found");
    await expect(dictation(page)).toHaveScreenshot(`settings-nomic-${theme}.png`);
  });
}
