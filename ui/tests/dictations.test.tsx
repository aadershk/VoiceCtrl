import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import { createMockHost } from "../src/bridge/mock";
import type { MockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installDictationsMock } from "../src/hub/mockDictations";
import type { DictationsScenario } from "../src/hub/mockDictations";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

async function openDictations(scenario: DictationsScenario = {}, talk = false): Promise<MockHost> {
  const host = createMockHost(
    talk ? { status: { kind: "ready", hotkey: "Ctrl", paused: false, offlineFallback: false, talkKeys: ["Left Ctrl", "Left Win"], provider: "Gemini" } } : {},
  );
  installDictationsMock(host, scenario);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Hub bridge={bridge} snapshot={ready} />);
  await settle();
  await settle();
  return host;
}

const calls = (host: MockHost, name: string) => host.calls.filter((c) => c.name === name).map((c) => c.args);
const toast = () => screen.getByRole("status");

beforeEach(() => {
  // A fixed "now" at 09:50, so the fixtures read as they do on the board.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 8, 29, 9, 50));
});

afterEach(() => {
  vi.useRealTimers();
});

describe("Dictations", () => {
  it("shows the latest as the hero, then the rest in day groups", async () => {
    await openDictations();

    const hero = screen.getByRole("region", { name: "Latest Transcription" });
    expect(hero.textContent).toContain("into Outlook · 2 min ago");
    expect(screen.getByRole("heading", { name: "Today" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Yesterday" })).toBeTruthy();
    const today = screen.getByRole("region", { name: "Today" });
    expect(within(today).queryByText(/Hi Maya/)).toBeNull();
    // The time follows the user's locale (this machine's here).
    expect(within(today).getByText(/^09:12( am)? · Notion$/i)).toBeTruthy();
  });

  it("pastes the latest into the app used before the Hub, and says where", async () => {
    const host = await openDictations();

    fireEvent.click(within(screen.getByRole("region", { name: "Latest Transcription" })).getByRole("button", { name: "Paste Again" }));
    await settle();

    expect(calls(host, "dictations.pasteAgain")).toEqual([{ id: null }]);
    expect(toast().textContent).toContain("Pasted into Outlook");
  });

  it("copies a row, and its icon says Copied", async () => {
    const host = await openDictations();
    const today = screen.getByRole("region", { name: "Today" });

    fireEvent.click(within(today).getAllByRole("button", { name: "Copy" })[0]!);
    await settle();

    expect(calls(host, "dictations.copy")).toEqual([{ id: "d-notion" }]);
    expect(within(today).getByRole("button", { name: "Copied" })).toBeTruthy();
  });

  it("deletes a row at once, and Undo brings it back", async () => {
    const host = await openDictations();

    fireEvent.click(within(screen.getByRole("region", { name: "Today" })).getAllByRole("button", { name: "Delete" })[0]!);
    await settle();
    expect(screen.queryByText(/load factor/)).toBeNull();
    expect(toast().textContent).toContain("Dictation deleted");

    fireEvent.click(within(toast()).getByRole("button", { name: "Undo" }));
    await settle();

    expect(calls(host, "dictations.undoDelete")).toEqual([{ id: "d-notion" }]);
    expect(screen.getByText(/load factor/)).toBeTruthy();
  });

  it("searches every kept Dictation, hides the hero while searching, and says when nothing matches", async () => {
    await openDictations();
    const search = screen.getByRole("searchbox", { name: "Search Dictations" });

    fireEvent.change(search, { target: { value: "maya" } });
    expect(screen.queryByRole("region", { name: "Latest Transcription" })).toBeNull();
    expect(screen.getByText(/Hi Maya/)).toBeTruthy();

    fireEvent.change(search, { target: { value: "invoice" } });
    expect(screen.getByRole("heading", { name: "No Dictations match “invoice”" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Clear search" }));
    expect(screen.getByRole("region", { name: "Latest Transcription" })).toBeTruthy();
  });

  it("clears all only after the dialog, then shows the first-run state", async () => {
    const host = await openDictations({ failed: 0 });

    fireEvent.click(screen.getByRole("button", { name: "More" }));
    await settle();
    fireEvent.click(screen.getByRole("menuitem", { name: "Clear all Dictations" }));
    await settle();
    const dialog = screen.getByRole("dialog", { name: "Clear all Dictations?" });
    expect(dialog.textContent).toContain("This removes all 7 Dictations from this PC.");

    fireEvent.click(within(dialog).getByRole("button", { name: "Clear all" }));
    await settle();
    await settle();

    expect(calls(host, "dictations.clearAll")).toHaveLength(1);
    expect(screen.getByRole("heading", { name: "Your Dictations will show up here" })).toBeTruthy();
  });

  it("copies all as text and says how many", async () => {
    await openDictations();

    fireEvent.click(screen.getByRole("button", { name: "More" }));
    await settle();
    fireEvent.click(screen.getByRole("menuitem", { name: "Copy all as text" }));
    await settle();

    expect(toast().textContent).toContain("Copied 7 Dictations as text");
  });
});

describe("Failed Dictations", () => {
  it("shows one notice per kept failure, with Retry offline only when a model is installed", async () => {
    await openDictations({ failed: 2, noOfflineModel: true });

    const notices = screen.getAllByRole("alert");
    expect(notices).toHaveLength(2);
    expect(notices[0]!.textContent).toContain("One Dictation didn't go through: no internet");
    expect(notices[0]!.textContent).toMatch(/Slack · 09:41( am)? · 12 s of audio/i);
    expect(screen.queryByRole("button", { name: "Retry offline" })).toBeNull();
  });

  it("offers Recover text for a cancelled recording (VED-76)", async () => {
    await openDictations({ cancelled: true, noOfflineModel: false });

    const notice = screen.getByRole("alert");
    expect(notice.textContent).toContain("You cancelled a recording");
    expect(screen.getByRole("button", { name: "Recover text" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Recover offline" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Retry" })).toBeNull();
  });

  it("retries, shows Transcribing meanwhile, then says where the result is", async () => {
    vi.useFakeTimers({ toFake: ["Date", "setTimeout"] });
    vi.setSystemTime(new Date(2026, 8, 29, 9, 50));
    const host = createMockHost();
    installDictationsMock(host);
    const bridge = createBridge(host.transport);
    const ready = bridge.ready();
    await act(() => vi.advanceTimersByTimeAsync(1));
    render(<Hub bridge={bridge} snapshot={await ready} />);
    await act(() => vi.advanceTimersByTimeAsync(10));

    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    await act(() => vi.advanceTimersByTimeAsync(10));
    expect(screen.getByRole("alert").textContent).toContain("Transcribing");

    await act(() => vi.advanceTimersByTimeAsync(1500));

    expect(screen.queryByRole("alert")).toBeNull();
    expect(toast().textContent).toContain("Transcribed. It's at the top, ready to paste.");
    expect(screen.getByRole("region", { name: "Latest Transcription" }).textContent).toContain("export bug");
  });

  it("dismisses a failure and says the audio is gone", async () => {
    const host = await openDictations();

    fireEvent.click(screen.getByRole("button", { name: "Dismiss and drop the audio" }));
    await settle();

    expect(calls(host, "dictations.dismiss")).toEqual([{ id: "f-slack" }]);
    expect(screen.queryByRole("alert")).toBeNull();
    expect(toast().textContent).toContain("Dismissed. The audio is gone.");
  });
});

describe("Keep Dictations", () => {
  it("None: says Dictations aren't kept, and Change in Settings goes to the row", async () => {
    await openDictations({ keep: "none", failed: 0 });

    expect(screen.getByText("Not kept. Audio is never saved either.")).toBeTruthy();
    expect(screen.queryByRole("searchbox")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Change in Settings" }));
    await settle();

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Settings");
  });

  it("first run with the Trigger Key on: teaches the hold first, the double-tap second", async () => {
    await openDictations({ dictations: "empty" }, true);

    expect(screen.getByText(/anywhere and speak, then let go/)).toBeTruthy();
    expect(screen.getByText(/double-tap/).textContent).toMatch(/bring up the bar and click it/);
  });

  it("first run: teaches the Hotkey, and a failure still shows above it", async () => {
    await openDictations({ dictations: "empty" });

    expect(screen.getByRole("heading", { name: "Your Dictations will show up here" })).toBeTruthy();
    expect(screen.getByText(/to bring up the bar, click it and speak/)).toBeTruthy();
    expect(screen.getByRole("alert")).toBeTruthy();
  });
});

describe("Dictations, Restore original", () => {
  it("offers it on a Dictation Smart cleanup edited, and nowhere else", async () => {
    await openDictations({ original: true });

    // The Latest hero and the one older row.
    expect(screen.getAllByRole("button", { name: "Restore original" })).toHaveLength(2);
  });

  it("has no Restore original when Smart cleanup left everything alone", async () => {
    await openDictations();

    expect(screen.queryByRole("button", { name: "Restore original" })).toBeNull();
  });

  it("restores through the host and says what happened", async () => {
    const host = await openDictations({ original: true });

    fireEvent.click(screen.getAllByRole("button", { name: "Restore original" })[0] as HTMLElement);
    await settle();

    expect(calls(host, "dictations.restoreOriginal")).toEqual([{ id: "d-outlook" }]);
    expect(toast().textContent).toContain("Original restored");
    expect(screen.getAllByRole("button", { name: "Restore original" })).toHaveLength(1);
  });
});
