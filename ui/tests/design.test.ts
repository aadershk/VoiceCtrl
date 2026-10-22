import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { iconMarkup, iconNames, icons } from "../src/design/icons";

const design = (file: string) => readFileSync(resolve(import.meta.dirname, "../src/design", file), "utf8");

/** The declarations inside the first rule whose selector list starts with `selector`. */
function block(css: string, selector: string): Map<string, string> {
  const start = css.indexOf(`${selector}`);
  const body = css.slice(css.indexOf("{", start) + 1, css.indexOf("}", start));
  const declarations = new Map<string, string>();
  for (const match of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) declarations.set(match[1]!, match[2]!.trim());
  return declarations;
}

// DESIGN §2 colour tokens that must differ, or deliberately match, per Theme.
const THEMED = [
  "--bg", "--panel", "--tint", "--line", "--line-2", "--ink", "--ink-2", "--ink-3", "--hover", "--btn",
  "--btn-ink", "--tile", "--coral", "--warn-bg", "--warn-line", "--warn-ink", "--warn-icon", "--danger",
  "--danger-ink", "--scrim", "--ov-bg", "--ov-line", "--shadow", "--nav-shadow", "--pop-shadow", "--ov-shadow",
];

describe("tokens.css", () => {
  const css = design("tokens.css");
  const light = block(css, ":root,\n[data-theme=\"light\"]");
  const dark = block(css, "[data-theme=\"dark\"]");

  it("defines every colour and elevation token in both Themes", () => {
    for (const token of THEMED) {
      expect(light.get(token), `${token} (Light)`).toBeTruthy();
      expect(dark.get(token), `${token} (Dark)`).toBeTruthy();
    }
  });

  it("keeps the key values from DESIGN §2", () => {
    expect(light.get("--bg")).toBe("#F3EDE4");
    expect(dark.get("--bg")).toBe("#121317");
    expect(light.get("--coral")).toBe("#F0472B");
    expect(dark.get("--btn")).toBe("#F0472B");
    expect(dark.get("--btn-ink")).toBe("#16171C");
    // The owner's Dark Failed notice (§13 reconciliation).
    expect(dark.get("--warn-bg")).toBe("#0F1013");
    expect(dark.get("--warn-line")).toBe("#282931");
  });

  it("applies the same reduced-motion overrides through the media query and the host attribute", () => {
    const media = block(css.slice(css.indexOf("@media (prefers-reduced-motion")), ":root");
    const attribute = block(css, ":root[data-reduced-motion=\"true\"]");
    expect(media.size).toBeGreaterThan(0);
    expect(Object.fromEntries(attribute)).toEqual(Object.fromEntries(media));
    expect(media.get("--dur-shape")).toBe("0ms");
    expect(media.get("--move")).toBe("0px");
  });
});

describe("component styles", () => {
  it("use tokens only: no raw colours (DESIGN §1 principle 5)", () => {
    for (const file of ["components/controls.css", "components/surfaces.css", "base.css"]) {
      const withoutComments = design(file).replace(/\/\*[\s\S]*?\*\//g, "");
      expect(withoutComments, file).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(/);
    }
  });

  it("never animate layout properties (§10 rule 4)", () => {
    for (const file of ["components/controls.css", "components/surfaces.css"]) {
      const transitions = design(file).match(/transition(?:-property)?:[^;]+;/g) ?? [];
      for (const transition of transitions) expect(transition, file).not.toMatch(/\b(width|height|top|left|right|bottom|margin|padding)\b/);
    }
  });
});

describe("icons", () => {
  it("include every icon the surfaces need", () => {
    const needed = [
      "dictations", "dictionary", "snippets", "profiles", "settings", // Hub nav
      "window", "copy", "pause", "play", "power", // tray flyout (§12)
      "minimize", "maximize", "close", "alert", "retry", "paste", "gift", "external", "cloud", "chip",
    ];
    for (const name of needed) expect(iconNames).toContain(name);
  });

  it("are stroke-only shapes on the 24 grid, with no fills or inline styles", () => {
    for (const [name, body] of Object.entries(icons)) {
      expect(body, name).toMatch(/^(<(path|circle|rect)\s[^>]*\/>)+$/);
      expect(body, name).not.toMatch(/fill=|style=|stroke=/);
    }
  });

  it("render as decorative svg for the Overlay page", () => {
    const svg = iconMarkup("close", 14);
    expect(svg).toContain('class="vc-icon vc-icon-14"');
    expect(svg).toContain('aria-hidden="true"');
    expect(svg).toContain('viewBox="0 0 24 24"');
  });
});
