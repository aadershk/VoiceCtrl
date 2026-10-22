import { describe, expect, it } from "vitest";
import { findIndex, MAX_RESULTS, searchQuickFind } from "../src/hub/findIndex";
import { rowDomId, settingsRows } from "../src/hub/screens";

// Ported from v3's HubSearchIndexTests, then the v4 rows (research 10, DESIGN §12).
describe("Quick Find index", () => {
  it.each([null, undefined, "", "   "])("returns nothing for an empty query (%j)", (query) => {
    expect(searchQuickFind(query)).toEqual([]);
  });

  it("matches a screen by name, the screen first", () => {
    const [first] = searchQuickFind("Dictionary");
    expect(first).toMatchObject({ label: "Dictionary", where: "Screen", screen: "dictionary" });
    expect(first?.row).toBeUndefined();
  });

  it("matches a Settings row and says where it lives", () => {
    expect(searchQuickFind("Theme")[0]).toMatchObject({ label: "Theme", where: "Settings", screen: "settings", row: "theme" });
  });

  it("is case-insensitive", () => {
    expect(searchQuickFind("ONLINE PROV").map((e) => e.label)).toEqual(["Online provider"]);
    // Every provider's name finds the provider row (ticket 60).
    expect(searchQuickFind("nvidia")[0]?.label).toBe("Online provider");
  });

  it("matches part of a word", () => {
    expect(searchQuickFind("api").map((e) => e.label)).toContain("API key");
  });

  it("returns an empty list, not null, when nothing matches", () => {
    expect(searchQuickFind("zzz-no-such-thing")).toEqual([]);
  });

  it.each([
    ["dark mode", "Theme"],
    ["word for word", "Default cleanup"],
    ["autostart", "Start VoiceCtrl with Windows"],
    ["updates", "Automatic updates"],
    ["version", "Automatic updates"],
    ["report a problem", "Copy diagnostics"],
  ])("finds %j through its alias", (query, label) => {
    expect(searchQuickFind(query)[0]?.label).toBe(label);
  });

  it("ranks label hits above alias hits, and starts above middles", () => {
    // "Mode" and "Move an app" start with "mo"; in "Smart cleanup model" and "Offline models" a later
    // word does; Smart cleanup has it only in its alias "local model" and Theme only in "dark mode".
    expect(searchQuickFind("mo").map((e) => e.label)).toEqual([
      "Mode",
      "Move an app to another kind",
      "Smart cleanup model",
      "Offline models",
      "Smart cleanup",
      "Theme",
    ]);
  });

  it("collapses spaces in the query", () => {
    expect(searchQuickFind("  sound   cues ")[0]?.label).toBe("Sound Cues");
  });

  it(`shows at most ${MAX_RESULTS}`, () => {
    expect(searchQuickFind("e")).toHaveLength(MAX_RESULTS);
  });

  it("covers every Settings row once, each with a distinct landing id", () => {
    const rows = findIndex.filter((e) => e.screen === "settings" && e.row);
    // Other's address shows only while Other is chosen, so Quick Find can't land on it.
    expect(rows.map((e) => e.row)).toEqual(settingsRows.filter((r) => r.findable !== false).map((r) => r.id));
    expect(new Set(settingsRows.map((r) => rowDomId(r.id))).size).toBe(settingsRows.length);
  });

  it("includes the rows DESIGN §12 added and drops the retired New-version notice", () => {
    const labels = findIndex.map((e) => e.label);
    expect(labels).toEqual(expect.arrayContaining(["Start VoiceCtrl with Windows", "Automatic updates", "Copy diagnostics"]));
    expect(labels).not.toContain("New-version notice");
  });
});
