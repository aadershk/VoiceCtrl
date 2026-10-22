import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const css = readFileSync(resolve(import.meta.dirname, "../src/overlay/overlay.css"), "utf8");

/**
 * The Overlay window stays alive and transparent while hidden, so any infinite CSS animation that
 * keeps running in a layer that isn't showing wakes the renderer and the GPU forever. VED-49
 * measured that as about 7% of a core at idle; pausing them outside the active layer took it to
 * under 1%. jsdom doesn't compute animations, so this guards the stylesheet itself.
 */
describe("overlay.css idle cost", () => {
  const infinite = [...css.matchAll(/([^{}]+)\{[^{}]*animation:[^;{}]*infinite[^;{}]*;/g)].map((m) => m[1]!.trim().split("\n").pop()!.trim());

  it("has infinite animations to guard (else this test is stale)", () => {
    expect(infinite.length).toBeGreaterThanOrEqual(3);
  });

  it("pauses every one of them outside the layer that is on", () => {
    const paused = [...css.matchAll(/([^{}]+)\{[^{}]*animation-play-state:\s*paused/g)].map((m) => m[1]!).join(",");
    for (const selector of infinite) {
      const target = selector.split(" ").pop()!; // .ov-wave .ov-bar -> .ov-bar
      expect(paused, `${selector} needs a :not(.on) paused rule`).toContain(`.ov-layer:not(.on) ${target}`);
    }
  });
});
