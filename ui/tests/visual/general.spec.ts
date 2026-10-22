import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// The end of the Settings page (slice 23): Sound and Overlay, Privacy, App and the About card, at
// the Hub's default size on the mock host; see src/hub/mockGeneral.ts for the address fixtures.
// The clock is fixed at 09:50, so "Checked today at 09:41" reads the same every run.
test.use({ viewport: { width: 1040, height: 900 } });

async function openSettings(page: Page, query: string): Promise<void> {
  await page.clock.setFixedTime(new Date(2026, 8, 30, 9, 50));
  await page.goto(`/hub.html?level=0.55&step=100000&${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await page.getByRole("navigation", { name: "Hub" }).getByRole("button", { name: "Settings" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Settings");
  await expect(page.getByRole("region", { name: "About VoiceCtrl" })).toBeAttached();
  // Let the screen change (DESIGN §10) finish.
  await page.waitForTimeout(600);
}

const group = (page: Page, name: string) => page.getByRole("region", { name, exact: true });

/** The App group and the About card together, as the page ends. */
async function appAndAbout(page: Page) {
  const about = page.getByRole("region", { name: "About VoiceCtrl" });
  await about.scrollIntoViewIfNeeded();
  const top = await group(page, "App").boundingBox();
  const bottom = await about.boundingBox();
  if (!top || !bottom) throw new Error("App group or About card not laid out");
  // Whole pixels: the page is scrolled to a fractional offset, and a fractional clip shifts the text
  // by half a pixel from one run to the next.
  const y = Math.round(top.y);
  return { x: Math.round(top.x), y, width: Math.ceil(top.width), height: Math.ceil(bottom.y + bottom.height) - y };
}

for (const theme of ["light", "dark"] as const) {
  test(`sound and overlay, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&sound=1`);
    await group(page, "Sound and Overlay").scrollIntoViewIfNeeded();
    await expect(group(page, "Sound and Overlay")).toHaveScreenshot(`general-sound-overlay-${theme}.png`);
  });

  test(`privacy, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}`);
    await group(page, "Privacy").scrollIntoViewIfNeeded();
    await expect(group(page, "Privacy")).toHaveScreenshot(`general-privacy-${theme}.png`);
  });

  test(`privacy, diagnostics copied, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}`);
    await group(page, "Privacy").scrollIntoViewIfNeeded();
    await group(page, "Privacy").getByRole("button", { name: "Copy" }).click();
    await expect(page.getByText("Diagnostics copied. Paste them into a GitHub issue.")).toBeVisible();
    await page.waitForTimeout(400);
    await expect(page).toHaveScreenshot(`general-diagnostics-copied-${theme}.png`);
  });

  test(`keep dictations confirm, ${theme}`, async ({ page }) => {
    await openSettings(page, `theme=${theme}&removes=12`);
    await group(page, "Privacy").scrollIntoViewIfNeeded();
    await group(page, "Privacy").getByRole("radio", { name: "None" }).click();
    await expect(page.getByRole("dialog", { name: "Stop keeping Dictations?" })).toBeVisible();
    await page.waitForTimeout(400);
    await expect(page).toHaveScreenshot(`general-keep-confirm-${theme}.png`);
  });

  for (const update of ["upToDate", "checking", "downloading:0.62", "ready", "off", "available", "failed"]) {
    const name = update.split(":")[0];
    test(`app and about, updates ${name}, ${theme}`, async ({ page }) => {
      await openSettings(page, `theme=${theme}&update=${update}`);
      const clip = await appAndAbout(page);
      await expect(page).toHaveScreenshot(`general-app-${name}-${theme}.png`, { clip });
    });
  }
}
