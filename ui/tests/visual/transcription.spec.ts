import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// The Settings page's Transcription group (slice 22) at the Hub's default size, on the mock host;
// see src/hub/mockTranscription.ts for the address fixtures. Downloads are held still.
test.use({ viewport: { width: 1040, height: 900 } });

async function openSettings(page: Page, query: string): Promise<void> {
  await page.goto(`/hub.html?level=0.55&step=100000&${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await page.getByRole("navigation", { name: "Hub" }).getByRole("button", { name: "Settings" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Settings");
  await expect(page.getByRole("radio", { name: "Auto" })).toBeVisible();
  // Let the screen change (DESIGN §10) finish.
  await page.waitForTimeout(600);
}

const transcription = (page: Page) => page.getByRole("region", { name: "Transcription" });

for (const theme of ["light", "dark"] as const) {
  test(`transcription, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&key=works`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-${theme}.png`);
  });

  test(`transcription, no key and Online, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&key=none&mode=online`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-nokey-${theme}.png`);
  });

  test(`transcription, key editing, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&key=works`);
    await page.getByRole("button", { name: "Change Gemini API key" }).click();
    await page.getByLabel("Gemini API key").fill("AIzaSyD-1234567890abcdef");
    await expect(transcription(page)).toHaveScreenshot(`transcription-key-editing-${theme}.png`);
  });

  test(`transcription, key refused, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&key=works`);
    await page.getByRole("button", { name: "Change Gemini API key" }).click();
    await page.getByLabel("Gemini API key").fill("AIzaTypo");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("status").filter({ hasText: "That key didn't work · Try another" })).toBeVisible();
    await expect(transcription(page)).toHaveScreenshot(`transcription-key-refused-${theme}.png`);
  });

  test(`transcription, saved key Gemini refused later, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&key=rejected`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-key-rejected-${theme}.png`);
  });

  test(`transcription, pack menu, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}`);
    await page.getByRole("button", { name: /^Offline language pack:/ }).click();
    await expect(page.getByRole("menu", { name: "Offline language pack" })).toBeVisible();
    await page.waitForTimeout(400);
    await expect(page).toHaveScreenshot(`transcription-pack-menu-${theme}.png`);
  });

  test(`transcription, pack downloading, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&dl=allLanguages:0.4&pack=none`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-pack-downloading-${theme}.png`);
  });

  test(`transcription, pack download stopped, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&dlFailed=european:0.3`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-pack-stopped-${theme}.png`);
  });

  test(`transcription, pack installed, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&pack=european&cleanup=verbatim&mode=offline`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-pack-installed-${theme}.png`);
  });
}

// Smart cleanup (ticket 37): the switch and the model row in each state it can be in.
for (const theme of ["light", "dark"] as const) {
  test(`transcription, Smart cleanup installed, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&smart=better&mode=offline`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-smart-installed-${theme}.png`);
  });

  test(`transcription, Smart cleanup downloading, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&smartDl=better:0.4`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-smart-downloading-${theme}.png`);
  });

  test(`transcription, Smart cleanup download stopped, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&smartDlFailed=standard:0.3`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-smart-stopped-${theme}.png`);
  });

  test(`transcription, Smart cleanup off, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&smart=off`);
    await expect(transcription(page)).toHaveScreenshot(`transcription-smart-off-${theme}.png`);
  });
}
