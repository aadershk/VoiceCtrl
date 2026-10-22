import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import type { ImportResult } from "../src/bridge/contracts.g";
import { createMockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installPersonalizationMock } from "../src/hub/mockPersonalization";
import type { PersonalizationScenario } from "../src/hub/mockPersonalization";
import { SNIPPET, WORD, count, exportToast, importToast, snippetError, wordError, wordMeta } from "../src/hub/personalizationCopy";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

async function openScreen(name: "Dictations" | "Dictionary" | "Snippets", scenario: PersonalizationScenario = {}) {
  const host = createMockHost();
  installPersonalizationMock(host, scenario);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Hub bridge={bridge} snapshot={ready} />);
  fireEvent.click(within(screen.getByRole("navigation", { name: "Hub" })).getByRole("button", { name }));
  await settle();
  return host;
}

const rows = (list: string) => within(screen.getByRole("region", { name: list }));
const toast = () => screen.getByRole("status");

describe("Personalization copy", () => {
  it("counts in the singular and the plural", () => {
    expect(count(1, WORD)).toBe("1 word");
    expect(count(14, WORD)).toBe("14 words");
    expect(count(1200, SNIPPET)).toBe("1,200 snippets");
  });

  it("says what a word is also heard as", () => {
    expect(wordMeta(["blue peak", "blu peak"])).toBe("also heard as “blue peak, blu peak”");
    expect(wordMeta([])).toBe("spelled exactly like this");
  });

  it("puts each error under its field", () => {
    expect(wordError("alreadyThere", " Bluepeak ")).toEqual({ field: "word", message: "Bluepeak is already in your Dictionary." });
    expect(wordError("heardAsTooMany", "x")?.field).toBe("alsoHeardAs");
    expect(wordError("none", "x")).toBeNull();
    expect(snippetError("alreadyThere", "my address")?.message).toBe("“my address” is already a snippet.");
    expect(snippetError("expansionTooLong", "x")).toEqual({ field: "expansion", message: "Keep it to 2,000 characters or fewer." });
  });

  it("reports an import the way the board does, adding only what applies", () => {
    const base: ImportResult = { outcome: "imported", added: 12, alreadyThere: 3, notAddedFull: 0, rejectedLines: [] };
    expect(importToast(base, WORD, "the Dictionary")?.message).toBe("Imported 12 words. 3 were already there.");
    expect(importToast({ ...base, added: 1, alreadyThere: 1 }, WORD, "the Dictionary")?.message).toBe(
      "Imported 1 word. 1 was already there.",
    );
    expect(importToast({ ...base, added: 0, alreadyThere: 0, notAddedFull: 2 }, WORD, "the Dictionary")?.message).toBe(
      "Nothing new to import. 2 didn't fit: the Dictionary holds 200.",
    );
    expect(importToast({ ...base, alreadyThere: 0, rejectedLines: [4] }, WORD, "the Dictionary")?.message).toBe(
      "Imported 12 words. Line 4 couldn't be read.",
    );
    expect(importToast({ ...base, alreadyThere: 0, rejectedLines: [4, 9, 12] }, WORD, "the Dictionary")?.message).toBe(
      "Imported 12 words. Lines 4, 9 and 12 couldn't be read.",
    );
    expect(importToast({ ...base, alreadyThere: 0, rejectedLines: [4, 9, 12, 20, 21] }, WORD, "the Dictionary")?.message).toBe(
      "Imported 12 words. Lines 4, 9, 12 and 2 more couldn't be read.",
    );
  });

  it("an import that failed is an error toast; a cancelled one says nothing", () => {
    const failed: ImportResult = { outcome: "failed", added: 0, alreadyThere: 0, notAddedFull: 0, rejectedLines: [] };
    expect(importToast(failed, WORD, "the Dictionary")).toEqual({
      message: "That file couldn't be imported. Choose a .csv file exported from VoiceCtrl.",
      error: true,
    });
    expect(importToast({ ...failed, outcome: "cancelled" }, WORD, "the Dictionary")).toBeNull();
  });

  it("says where an export went", () => {
    expect(exportToast({ outcome: "exported", count: 14, folder: "Downloads" }, WORD)?.message).toBe("Exported 14 words to Downloads");
    expect(exportToast({ outcome: "failed", count: 0, folder: "Downloads" }, WORD)?.error).toBe(true);
    expect(exportToast({ outcome: "cancelled", count: 0, folder: "" }, WORD)).toBeNull();
  });
});

describe("Dictionary screen", () => {
  it("lists the words with their count and what each is also heard as", async () => {
    await openScreen("Dictionary");

    expect(screen.getByRole("heading", { name: "5 words" })).toBeTruthy();
    expect(rows("Your words").getByText("Bluepeak")).toBeTruthy();
    expect(rows("Your words").getByText("also heard as “blue peak”")).toBeTruthy();
    expect(rows("Your words").getByText("spelled exactly like this")).toBeTruthy();
  });

  it("Filter narrows the list, also by what a word is heard as, and says when nothing matches", async () => {
    await openScreen("Dictionary");

    fireEvent.change(screen.getByRole("searchbox", { name: "Filter words" }), { target: { value: "kaiser" } });
    expect(rows("Your words").getByText("Keizersgracht")).toBeTruthy();
    expect(rows("Your words").queryByText("Bluepeak")).toBeNull();

    fireEvent.change(screen.getByRole("searchbox", { name: "Filter words" }), { target: { value: "zzz" } });
    expect(screen.getByText("No words match “zzz”")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Clear filter" }));
    expect(rows("Your words").getByText("Bluepeak")).toBeTruthy();
  });

  it("Add word saves on top and says so", async () => {
    const host = await openScreen("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Add word" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Word" }), { target: { value: "Schiphol" } });
    fireEvent.change(screen.getByRole("textbox", { name: "Also heard as" }), { target: { value: "sky pool" } });
    fireEvent.click(screen.getByRole("button", { name: "Save word" }));
    await settle();

    expect(host.calls.find((c) => c.name === "dictionary.save")?.args).toEqual({ original: null, word: "Schiphol", alsoHeardAs: "sky pool" });
    expect(screen.queryByRole("textbox", { name: "Word" })).toBeNull();
    expect(screen.getByRole("heading", { name: "6 words" })).toBeTruthy();
    expect(toast().textContent).toContain("Saved Schiphol");
  });

  it("shows the host's error under the Word field and keeps the editor open", async () => {
    await openScreen("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Add word" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Word" }), { target: { value: "bluepeak" } });
    fireEvent.click(screen.getByRole("button", { name: "Save word" }));
    await settle();

    const word = screen.getByRole("textbox", { name: "Word" });
    expect(word.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByRole("alert").textContent).toBe("bluepeak is already in your Dictionary.");

    // Typing again clears the error.
    fireEvent.change(word, { target: { value: "bluepeaks" } });
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("an empty word asks for one", async () => {
    await openScreen("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Add word" }));
    fireEvent.click(screen.getByRole("button", { name: "Save word" }));
    await settle();

    expect(screen.getByRole("alert").textContent).toBe("Type the word first.");
  });

  it("Edit opens the editor in place of the row, filled in; Esc closes it", async () => {
    await openScreen("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Edit Bluepeak" }));
    const word = screen.getByRole("textbox", { name: "Word" }) as HTMLInputElement;
    expect(word.value).toBe("Bluepeak");
    expect((screen.getByRole("textbox", { name: "Also heard as" }) as HTMLInputElement).value).toBe("blue peak");
    expect(rows("Your words").queryByText("also heard as “blue peak”")).toBeNull();

    fireEvent.keyDown(word, { key: "Escape" });
    expect(screen.queryByRole("textbox", { name: "Word" })).toBeNull();
    expect(rows("Your words").getByText("also heard as “blue peak”")).toBeTruthy();
  });

  it("Remove offers Undo, which puts the word back where it was", async () => {
    await openScreen("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Remove WebView2" }));
    await settle();
    expect(rows("Your words").queryByText("WebView2")).toBeNull();
    expect(toast().textContent).toContain("Removed WebView2");

    fireEvent.click(within(toast()).getByRole("button", { name: "Undo" }));
    await settle();
    const words = rows("Your words").getAllByText(/^(Bluepeak|WebView2|Aadersh)$/).map((el) => el.textContent);
    expect(words).toEqual(["Bluepeak", "WebView2", "Aadersh"]);
  });

  it("empty: explains the value and offers Import and Add word", async () => {
    await openScreen("Dictionary", { words: [] });

    const empty = screen.getByRole("heading", { name: "No words yet" }).closest("section");
    expect(empty).toBeTruthy();
    expect(within(empty as HTMLElement).getByRole("button", { name: "Import" })).toBeTruthy();
    fireEvent.click(within(empty as HTMLElement).getByRole("button", { name: "Add word" }));
    expect(screen.queryByRole("heading", { name: "No words yet" })).toBeNull();
    expect(screen.getByRole("textbox", { name: "Word" })).toBeTruthy();
  });

  it("Import reports what it did and shows the new words", async () => {
    await openScreen("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Import or export" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Import from a file" }));
    await settle();
    await settle();

    expect(toast().textContent).toContain("Imported 12 words. 3 were already there.");
    expect(rows("Your words").getByText("Maya Ortiz")).toBeTruthy();
  });

  it("a failed import is an error toast and changes nothing", async () => {
    await openScreen("Dictionary", { importAs: "failed" });

    fireEvent.click(screen.getByRole("button", { name: "Import or export" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Import from a file" }));
    await settle();

    expect(toast().textContent).toContain("That file couldn't be imported. Choose a .csv file exported from VoiceCtrl.");
    expect(screen.getByRole("heading", { name: "5 words" })).toBeTruthy();
  });

  it("Export says where the file went, and is off while the list is empty", async () => {
    await openScreen("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Import or export" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Export to a file" }));
    await settle();

    expect(toast().textContent).toContain("Exported 5 words to Downloads");
  });

  it("Quick Find lands on Import or export", async () => {
    await openScreen("Dictations");

    fireEvent.change(screen.getByRole("combobox", { name: "Quick Find" }), { target: { value: "import or export dictionary" } });
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Quick Find" }), { key: "Enter" });
    await settle();

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Dictionary");
    expect(document.getElementById("find-import-export")).toBeTruthy();
  });
});

describe("Snippets screen", () => {
  it("lists trigger → expansion, on one line", async () => {
    await openScreen("Snippets");

    expect(screen.getByRole("heading", { name: "4 snippets" })).toBeTruthy();
    expect(rows("Your snippets").getByText("my address")).toBeTruthy();
    expect(rows("Your snippets").getByText("Best regards, Vedarth")).toBeTruthy();
  });

  it("Add snippet keeps line breaks and says so", async () => {
    const host = await openScreen("Snippets");

    fireEvent.click(screen.getByRole("button", { name: "Add snippet" }));
    fireEvent.change(screen.getByRole("textbox", { name: "When you say" }), { target: { value: "brb" } });
    fireEvent.change(screen.getByRole("textbox", { name: "VoiceCtrl types" }), { target: { value: "be right\nback" } });
    fireEvent.click(screen.getByRole("button", { name: "Save snippet" }));
    await settle();

    expect(host.calls.find((c) => c.name === "snippets.save")?.args).toEqual({ original: null, trigger: "brb", expansion: "be right\nback" });
    expect(toast().textContent).toContain("Saved “brb”");
    expect(screen.getByRole("heading", { name: "5 snippets" })).toBeTruthy();
  });

  it("Ctrl+Enter in the expansion saves", async () => {
    await openScreen("Snippets");

    fireEvent.click(screen.getByRole("button", { name: "Add snippet" }));
    fireEvent.change(screen.getByRole("textbox", { name: "When you say" }), { target: { value: "omw" } });
    fireEvent.keyDown(screen.getByRole("textbox", { name: "VoiceCtrl types" }), { key: "Enter", ctrlKey: true });
    await settle();

    expect(toast().textContent).toContain("Saved “omw”");
  });

  it("a trigger with = is refused under its field", async () => {
    await openScreen("Snippets");

    fireEvent.click(screen.getByRole("button", { name: "Add snippet" }));
    fireEvent.change(screen.getByRole("textbox", { name: "When you say" }), { target: { value: "a = b" } });
    fireEvent.click(screen.getByRole("button", { name: "Save snippet" }));
    await settle();

    expect(screen.getByRole("alert").textContent).toBe("A trigger can't start with # or contain =.");
  });

  it("Remove offers Undo", async () => {
    await openScreen("Snippets");

    fireEvent.click(screen.getByRole("button", { name: "Remove “sign off”" }));
    await settle();
    expect(rows("Your snippets").queryByText("sign off")).toBeNull();

    fireEvent.click(within(toast()).getByRole("button", { name: "Undo" }));
    await settle();
    expect(rows("Your snippets").getByText("sign off")).toBeTruthy();
  });

  it("empty: explains the value and offers Import and Add snippet", async () => {
    await openScreen("Snippets", { snippets: [] });

    expect(screen.getByRole("heading", { name: "No snippets yet" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Import" })).toBeTruthy();
  });
});
