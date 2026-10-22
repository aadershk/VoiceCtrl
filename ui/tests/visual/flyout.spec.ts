import { expect, test } from "@playwright/test";

// The tray flyout of the approved VED-24 board (DESIGN §12), in Light and Dark, on the mock host.
// The menu itself is captured; the page around it is transparent in the app.

const states: Record<string, string> = {
  ready: "",
  paused: "status=paused",
  "no-latest": "latest=0&mode=offline",
  "key-warning": "status=apiKeyNotWorking&offline=1",
};

for (const theme of ["light", "dark"] as const) {
  for (const [name, query] of Object.entries(states)) {
    test(`flyout ${name}, ${theme}`, async ({ page }) => {
      await page.setViewportSize({ width: 420, height: 520 });
      await page.goto(`/flyout.html?theme=${theme}&${query}`);
      const menu = page.getByRole("menu", { name: "VoiceCtrl" });
      await menu.waitFor();
      await page.evaluate(() => document.fonts.ready);

      await expect(page.locator(".flyout")).toHaveScreenshot(`flyout-${name}-${theme}.png`);
    });
  }
}

test("flyout hover and keyboard focus, light", async ({ page }) => {
  await page.setViewportSize({ width: 420, height: 520 });
  await page.goto("/flyout.html?theme=light");
  await page.getByRole("menu", { name: "VoiceCtrl" }).waitFor();
  await page.evaluate(() => document.fonts.ready);

  await page.getByRole("menuitem", { name: "Settings" }).hover();
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("ArrowDown");

  await expect(page.locator(".flyout")).toHaveScreenshot("flyout-interaction-light.png");
});
