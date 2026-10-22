import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import { createMockHost } from "../src/bridge/mock";
import type { MockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installPersonalizationMock } from "../src/hub/mockPersonalization";
import type { PersonalizationScenario } from "../src/hub/mockPersonalization";
import { installSettingsMock } from "../src/hub/mockSettings";
import { installTranscriptionMock } from "../src/hub/mockTranscription";
import { rowDomId } from "../src/hub/screens";
import { SUGGESTIONS_ON, suggestionTitle } from "../src/hub/screens/SuggestionsCard";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

const wait = (ms: number) => act(() => new Promise((resolve) => setTimeout(resolve, ms)));

async function open(name: "Dictionary" | "Settings", scenario: PersonalizationScenario = {}): Promise<MockHost> {
  const host = createMockHost();
  installSettingsMock(host);
  installTranscriptionMock(host);
  installPersonalizationMock(host, scenario);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Hub bridge={bridge} snapshot={ready} />);
  fireEvent.click(within(screen.getByRole("navigation", { name: "Hub" })).getByRole("button", { name }));
  await settle();
  await settle();
  return host;
}

const card = () => within(screen.getByRole("region", { name: "Suggestions" }));
const words = () => within(screen.getByRole("region", { name: "Your words" }));

const row = (id: string) => {
  const el = document.getElementById(rowDomId(id));
  if (!el) throw new Error(`No row ${id}`);
  return within(el);
};

describe("Dictionary Suggestions card", () => {
  it("names how many there are", () => {
    expect(suggestionTitle(1)).toBe("1 suggestion");
    expect(suggestionTitle(3)).toBe("3 suggestions");
  });

  it("shows each suggestion with how many Dictations it was said in", async () => {
    await open("Dictionary");

    expect(card().getByText("3 suggestions")).toBeTruthy();
    expect(card().getByText("Found in your Dictations. Nothing is added until you say so.")).toBeTruthy();
    expect(card().getByText("6×")).toBeTruthy();
    expect(card().getByRole("button", { name: "Add Maya Ortiz" })).toBeTruthy();
    expect(card().getByRole("button", { name: "Ignore Lisbon Ops" })).toBeTruthy();
  });

  it("Add puts the word on top of the Dictionary and takes its chip away", async () => {
    const host = await open("Dictionary");

    fireEvent.click(card().getByRole("button", { name: "Add Maya Ortiz" }));
    await settle();

    expect(host.calls.find((c) => c.name === "dictionary.addSuggestion")?.args).toEqual({ word: "Maya Ortiz" });
    expect(screen.getByText("Added Maya Ortiz to your Dictionary")).toBeTruthy();
    expect(words().getByText("6 words")).toBeTruthy();
    expect(card().queryByRole("button", { name: "Add Maya Ortiz" })).toBeNull();
    expect(card().getByText("2 suggestions")).toBeTruthy();
  });

  it("Ignore takes the chip away without adding the word, and tells the host", async () => {
    const host = await open("Dictionary");

    fireEvent.click(card().getByRole("button", { name: "Ignore Glide" }));
    await settle();

    expect(host.calls.find((c) => c.name === "dictionary.ignoreSuggestion")?.args).toEqual({ word: "Glide" });
    expect(card().queryByText("Glide")).toBeNull();
    expect(words().getByText("5 words")).toBeTruthy();
  });

  it("goes away with its last chip", async () => {
    await open("Dictionary");

    for (const word of ["Maya Ortiz", "Lisbon Ops", "Glide"]) {
      fireEvent.click(card().getByRole("button", { name: `Ignore ${word}` }));
      await settle();
    }
    await wait(400);

    expect(screen.queryByRole("region", { name: "Suggestions" })).toBeNull();
  });

  it("hides a suggestion once the word is added by hand", async () => {
    await open("Dictionary");

    fireEvent.click(screen.getByRole("button", { name: "Add word" }));
    await settle();
    fireEvent.change(screen.getByRole("textbox", { name: "Word" }), { target: { value: "Glide" } });
    fireEvent.click(screen.getByRole("button", { name: "Save word" }));
    await settle();

    expect(card().queryByText("Glide")).toBeNull();
    expect(card().getByText("2 suggestions")).toBeTruthy();
  });

  it("shows nothing when there is nothing to suggest", async () => {
    await open("Dictionary", { suggestions: "none" });

    expect(screen.queryByRole("region", { name: "Suggestions" })).toBeNull();
  });

  it("shows nothing in Offline mode, where no suggestions are made", async () => {
    await open("Dictionary", { offline: true });

    expect(screen.queryByRole("region", { name: "Suggestions" })).toBeNull();
  });
});

describe("Dictionary Suggestions, off", () => {
  it("offers the dashed Turn on card, which turns suggestions on", async () => {
    const host = await open("Dictionary", { suggestions: "off" });

    expect(card().getByText("VoiceCtrl can suggest names and words you say often. Off, and never used in Offline mode.")).toBeTruthy();
    fireEvent.click(card().getByRole("button", { name: "Turn on" }));
    await settle();
    await wait(400);

    expect(host.calls.find((c) => c.name === "settings.setSuggestWords")?.args).toEqual({ on: true });
    expect(screen.getByText(SUGGESTIONS_ON)).toBeTruthy();
    expect(card().getByText("3 suggestions")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Turn on" })).toBeNull();
  });
});

describe("Settings, Word suggestions", () => {
  it("the switch shows and saves Suggest Dictionary words", async () => {
    const host = await open("Settings", { suggestions: "off" });

    const toggle = row("suggest-words").getByRole("switch", { name: "Suggest Dictionary words" });
    expect(toggle.getAttribute("aria-checked")).toBe("false");
    fireEvent.click(toggle);
    await settle();

    expect(host.calls.find((c) => c.name === "settings.setSuggestWords")?.args).toEqual({ on: true });
    expect(row("suggest-words").getByRole("switch").getAttribute("aria-checked")).toBe("true");
  });

  it("the TypeSafe key is optional: Not set, with Add key", async () => {
    await open("Settings");

    expect(row("typesafe-key").getByText("Not set")).toBeTruthy();
    expect(row("typesafe-key").getByText("Optional, for sharper suggestions. Sends recent Dictation text to TypeSafe.")).toBeTruthy();
    expect(row("typesafe-key").getByRole("button", { name: "Add TypeSafe key" })).toBeTruthy();
  });

  it("checks a key with TypeSafe before saving it, then shows it masked", async () => {
    const host = await open("Settings");

    fireEvent.click(row("typesafe-key").getByRole("button", { name: "Add TypeSafe key" }));
    await settle();
    fireEvent.change(screen.getByLabelText("TypeSafe key"), { target: { value: "ts_live_abcdefghijklmn9c2e" } });
    fireEvent.click(row("typesafe-key").getByRole("button", { name: "Save" }));
    await settle();
    expect(row("typesafe-key").getByText("Checking the key")).toBeTruthy();
    await wait(60);
    await settle();

    expect(host.calls.find((c) => c.name === "settings.saveTypeSafeKey")?.args).toEqual({ key: "ts_live_abcdefghijklmn9c2e" });
    expect(screen.getByText("Key saved. It works.")).toBeTruthy();
    expect(row("typesafe-key").getByText("ts_l ···· 9c2e")).toBeTruthy();
    expect(row("typesafe-key").getByText("Works")).toBeTruthy();
  });

  it("keeps the field and says so when TypeSafe refuses a key", async () => {
    await open("Settings");

    fireEvent.click(row("typesafe-key").getByRole("button", { name: "Add TypeSafe key" }));
    await settle();
    fireEvent.change(screen.getByLabelText("TypeSafe key"), { target: { value: "ts_typo" } });
    fireEvent.click(row("typesafe-key").getByRole("button", { name: "Save" }));
    await wait(60);
    await settle();

    expect(row("typesafe-key").getByRole("status").textContent).toBe("That key didn't work · Try another");
    expect((screen.getByLabelText("TypeSafe key") as HTMLInputElement).value).toBe("ts_typo");
  });

  it("says when a saved key was refused", async () => {
    await open("Settings", { typeSafeKey: "rejected" });

    expect(row("typesafe-key").getByRole("status").textContent).toBe("That key didn't work · Try another");
    expect(row("typesafe-key").getByRole("button", { name: "Try another TypeSafe key" })).toBeTruthy();
  });

  it("Remove asks first, then clears the key", async () => {
    const host = await open("Settings", { typeSafeKey: "works" });

    fireEvent.click(row("typesafe-key").getByRole("button", { name: "Remove TypeSafe key" }));
    await settle();
    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.getByText("Remove the TypeSafe key?")).toBeTruthy();
    fireEvent.click(dialog.getByRole("button", { name: "Remove key" }));
    await settle();
    await settle();

    expect(host.calls.some((c) => c.name === "settings.removeTypeSafeKey")).toBe(true);
    expect(row("typesafe-key").getByText("Not set")).toBeTruthy();
  });
});
