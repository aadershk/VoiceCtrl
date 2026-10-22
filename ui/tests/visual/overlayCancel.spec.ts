import { expect, test } from "@playwright/test";

// VED-68: the ✕ on the pill must cancel in a real layout. The unit tests fake elementFromPoint,
// so only a real browser shows whether the ✕ is hit-testable where the pointer lands.

test.use({ viewport: { width: 360, height: 120 } });

type Pt = { x: number; y: number };

async function stageOrigin(page: import("@playwright/test").Page): Promise<Pt> {
  const box = await page.locator("#stage").boundingBox();
  return { x: box!.x, y: box!.y };
}

for (const reducedMotion of ["reduce", "no-preference"] as const) {
  test.describe(`motion ${reducedMotion}`, () => {
    test.use({ reducedMotion });

    test("click the tile to start, then click the ✕", async ({ page }) => {
      await page.goto("/overlay.html?state=ready&theme=light");
      const o = await stageOrigin(page);
      // The Ready tile's centre on the bottom dock.
      const tile = { x: o.x + 160, y: o.y + 48 };
      await page.mouse.move(tile.x, tile.y);
      await page.mouse.down();
      await page.mouse.up();
      await expect(page.locator('[data-layer="listening"]')).toHaveClass(/on/);
      await page.waitForTimeout(600);
      // The hover pill is 124 wide, its ✕ at the right end.
      const cross = await page.locator('[data-layer="listening"] .ov-x').boundingBox();
      await page.mouse.move(cross!.x + cross!.width / 2, cross!.y + cross!.height / 2, { steps: 5 });
      await page.mouse.down();
      await page.mouse.up();
      await expect(page.locator('[data-layer="listening"]')).not.toHaveClass(/on/);
    });

    test("recording started elsewhere, pointer arrives at the pill, then ✕", async ({ page }) => {
      await page.goto("/overlay.html?state=ready&theme=light");
      const o = await stageOrigin(page);
      await page.mouse.move(o.x + 5, o.y + 5);
      await page.evaluate(() => (window as unknown as { overlayDemo: { show(p: string): void } }).overlayDemo.show("listening"));
      await expect(page.locator('[data-layer="listening"]')).toHaveClass(/on/);
      await page.waitForTimeout(600);
      // Resting pill is 54 wide: 133..187. Enter at its left, then travel to the ✕.
      await page.mouse.move(o.x + 140, o.y + 48, { steps: 3 });
      await page.waitForTimeout(600);
      const cross = await page.locator('[data-layer="listening"] .ov-x').boundingBox();
      await page.mouse.move(cross!.x + cross!.width / 2, cross!.y + cross!.height / 2, { steps: 8 });
      await page.mouse.down();
      await page.mouse.up();
      await expect(page.locator('[data-layer="listening"]')).not.toHaveClass(/on/);
    });
  });
}
