import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// Dictionary and Snippets (research 10) on the mock host, at the Hub's default size; see
// src/hub/mockPersonalization.ts for the address fixtures. Baselines are the screens the owner
// signed off, one per board state.
test.use({ viewport: { width: 1040, height: 680 } });

async function openScreen(page: Page, screen: "Dictionary" | "Snippets" | "Settings", query: string): Promise<void> {
  await page.goto(`/hub.html?${query}`);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
  await page.getByRole("navigation", { name: "Hub" }).getByRole("button", { name: screen }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(screen);
  // Moves the pointer off the sidebar, so no hover state is caught.
  await page.mouse.move(1000, 660);
}

for (const theme of ["light", "dark"] as const) {
  test(`dictionary, ${theme}`, async ({ page }) => {
    await openScreen(page, "Dictionary", `theme=${theme}`);
    await expect(page.getByRole("heading", { name: "5 words" })).toBeVisible();
    await expect(page).toHaveScreenshot(`dictionary-${theme}.png`);
  });

  test(`dictionary add word, duplicate, ${theme}`, async ({ page }) => {
    await openScreen(page, "Dictionary", `theme=${theme}`);
    await page.getByRole("button", { name: "Add word" }).click();
    await page.getByRole("textbox", { name: "Word" }).fill("Bluepeak");
    await page.getByRole("button", { name: "Save word" }).click();
    await expect(page.getByRole("alert")).toHaveText("Bluepeak is already in your Dictionary.");
    await expect(page).toHaveScreenshot(`dictionary-editor-error-${theme}.png`);
  });

  test(`dictionary empty, ${theme}`, async ({ page }) => {
    await openScreen(page, "Dictionary", `theme=${theme}&words=empty&sugg=off`);
    await expect(page.getByRole("heading", { name: "No words yet" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Turn on" })).toBeVisible();
    await expect(page).toHaveScreenshot(`dictionary-empty-${theme}.png`);
  });

  test(`dictionary suggestion added, ${theme}`, async ({ page }) => {
    await openScreen(page, "Dictionary", `theme=${theme}`);
    await page.getByRole("button", { name: "Add Maya Ortiz" }).click();
    await expect(page.getByText("Added Maya Ortiz to your Dictionary")).toBeVisible();
    await expect(page.getByRole("heading", { name: "6 words" })).toBeVisible();
    await expect(page.getByText("2 suggestions")).toBeVisible();
    await page.mouse.move(1000, 660);
    await expect(page).toHaveScreenshot(`dictionary-suggestion-added-${theme}.png`);
  });

  test(`settings word suggestions, ${theme}`, async ({ page }) => {
    await openScreen(page, "Settings", `theme=${theme}&tskey=works`);
    const group = page.getByRole("region", { name: "Word suggestions", exact: true });
    await group.scrollIntoViewIfNeeded();
    await expect(group.getByText("ts_l ···· 9c2e")).toBeVisible();
    await expect(group).toHaveScreenshot(`settings-word-suggestions-${theme}.png`);
  });

  test(`settings word suggestions, key refused, ${theme}`, async ({ page }) => {
    await openScreen(page, "Settings", `theme=${theme}&tskey=rejected&sugg=off`);
    const group = page.getByRole("region", { name: "Word suggestions", exact: true });
    await group.scrollIntoViewIfNeeded();
    await expect(group.getByRole("status")).toHaveText("That key didn't work · Try another");
    await expect(group).toHaveScreenshot(`settings-word-suggestions-refused-${theme}.png`);
  });

  test(`dictionary import menu, ${theme}`, async ({ page }) => {
    await openScreen(page, "Dictionary", `theme=${theme}`);
    await page.getByRole("button", { name: "Import or export" }).click();
    await expect(page.getByRole("menu", { name: "Import or export" })).toBeVisible();
    await expect(page).toHaveScreenshot(`dictionary-import-menu-${theme}.png`);
  });

  test(`snippets, ${theme}`, async ({ page }) => {
    await openScreen(page, "Snippets", `theme=${theme}`);
    await expect(page.getByRole("heading", { name: "4 snippets" })).toBeVisible();
    await expect(page).toHaveScreenshot(`snippets-${theme}.png`);
  });

  test(`snippets edit, ${theme}`, async ({ page }) => {
    await openScreen(page, "Snippets", `theme=${theme}`);
    await page.getByRole("button", { name: "Edit “sign off”" }).click();
    await expect(page.getByRole("textbox", { name: "When you say" })).toHaveValue("sign off");
    await expect(page).toHaveScreenshot(`snippets-editor-${theme}.png`);
  });

  test(`snippets empty, ${theme}`, async ({ page }) => {
    await openScreen(page, "Snippets", `theme=${theme}&snippets=empty`);
    await expect(page.getByRole("heading", { name: "No snippets yet" })).toBeVisible();
    await expect(page).toHaveScreenshot(`snippets-empty-${theme}.png`);
  });

  test(`snippets import failed, ${theme}`, async ({ page }) => {
    await openScreen(page, "Snippets", `theme=${theme}&import=failed`);
    await page.getByRole("button", { name: "Import or export" }).click();
    await page.getByRole("menuitem", { name: "Import from a file" }).click();
    await expect(page.getByRole("status")).toContainText("That file couldn't be imported.");
    await page.mouse.move(1000, 660);
    await expect(page).toHaveScreenshot(`snippets-import-failed-${theme}.png`);
  });
}
