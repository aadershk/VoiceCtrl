import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import { createMockHost } from "../src/bridge/mock";
import type { MockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installSettingsMock } from "../src/hub/mockSettings";
import { installTranscriptionMock } from "../src/hub/mockTranscription";
import type { TranscriptionScenario } from "../src/hub/mockTranscription";
import { rowDomId } from "../src/hub/screens";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

const wait = (ms: number) => act(() => new Promise((resolve) => setTimeout(resolve, ms)));

async function openSettings(scenario: TranscriptionScenario = {}): Promise<MockHost> {
  const host = createMockHost();
  installSettingsMock(host);
  installTranscriptionMock(host, scenario);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Hub bridge={bridge} snapshot={ready} />);
  fireEvent.click(within(screen.getByRole("navigation", { name: "Hub" })).getByRole("button", { name: "Settings" }));
  await settle();
  await settle();
  return host;
}

const row = (id: string) => {
  const el = document.getElementById(rowDomId(id));
  if (!el) throw new Error(`No row ${id}`);
  return within(el);
};

const GOOD_KEY = "AIzaSyD-1234567890abcdefghijklmnop9z8y";

async function type(label: string | RegExp, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
  await settle();
}

describe("Settings, Mode", () => {
  it("shows the saved Mode and saves another through the host", async () => {
    const host = await openSettings();

    expect(row("mode").getByRole("radio", { name: "Auto" }).getAttribute("aria-checked")).toBe("true");
    fireEvent.click(row("mode").getByRole("radio", { name: "Offline" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.setMode")?.args).toEqual({ mode: "offline" });
    expect(row("mode").getByRole("radio", { name: "Offline" }).getAttribute("aria-checked")).toBe("true");
  });

  it("says Online needs a key while none is saved, and only then", async () => {
    await openSettings({ mode: "online", key: "none" });
    expect(row("mode").getByRole("status").textContent).toBe("Online needs a Gemini API key. Add one below.");
  });

  it("has nothing to say about Online once a key is saved", async () => {
    await openSettings({ mode: "online", key: "works" });
    expect(row("mode").queryByRole("status")).toBeNull();
  });
});

describe("Settings, Default cleanup", () => {
  it("offers the four levels by their friendly names, Standard first chosen", async () => {
    await openSettings();

    const labels = row("default-cleanup").getAllByRole("radio").map((r) => r.textContent);
    expect(labels).toEqual(["Word for word", "Light", "Standard", "Thorough"]);
    expect(row("default-cleanup").getByRole("radio", { name: "Standard" }).getAttribute("aria-checked")).toBe("true");
  });

  it("saves the stored name of the level, not its label", async () => {
    const host = await openSettings();

    fireEvent.click(row("default-cleanup").getByRole("radio", { name: "Word for word" }));
    await settle();
    fireEvent.click(row("default-cleanup").getByRole("radio", { name: "Thorough" }));
    await settle();

    expect(host.calls.filter((c) => c.name === "settings.setDefaultCleanup").map((c) => c.args)).toEqual([
      { level: "verbatim" },
      { level: "aggressive" },
    ]);
    expect(row("default-cleanup").getByRole("radio", { name: "Thorough" }).getAttribute("aria-checked")).toBe("true");
  });

  it("shows a level nobody wrote as Standard", async () => {
    await openSettings({ cleanup: "hand-edited" });
    expect(row("default-cleanup").getByRole("radio", { name: "Standard" }).getAttribute("aria-checked")).toBe("true");
  });
});

describe("Settings, Gemini API key", () => {
  it("shows a working key masked, with Works", async () => {
    await openSettings({ key: "works" });

    expect(row("api-key").getByText("AIza ···· 3f9a")).toBeTruthy();
    expect(row("api-key").getByText("Works")).toBeTruthy();
    expect(row("api-key").getByRole("button", { name: "Change Gemini API key" })).toBeTruthy();
  });

  it("does not claim a key works before anything has checked it", async () => {
    await openSettings({ key: "saved" });

    expect(row("api-key").queryByText("Works")).toBeNull();
    expect(row("api-key").getByText("Saved")).toBeTruthy();
  });

  it("offers Add key when there is none", async () => {
    await openSettings({ key: "none" });

    expect(row("api-key").getByText("Not set")).toBeTruthy();
    expect(row("api-key").getByRole("button", { name: "Add Gemini API key" })).toBeTruthy();
  });

  it("says when Gemini refused the key, and offers to try another", async () => {
    await openSettings({ key: "rejected" });

    expect(row("api-key").getByRole("status").textContent).toBe("That key didn't work · Try another");
    expect(row("api-key").getByRole("button", { name: "Try another Gemini API key" })).toBeTruthy();
  });

  it("saves a key that works after 'Checking the key', and toasts", async () => {
    const host = await openSettings({ key: "none" });

    fireEvent.click(row("api-key").getByRole("button", { name: "Add Gemini API key" }));
    await settle();
    const field = screen.getByLabelText("Gemini API key") as HTMLInputElement;
    expect(field.type).toBe("password");
    expect(field).toBe(document.activeElement);
    await type("Gemini API key", GOOD_KEY);
    fireEvent.click(row("api-key").getByRole("button", { name: "Save" }));
    await settle();

    expect(row("api-key").getByText("Checking the key")).toBeTruthy();
    await wait(60);
    await settle();

    expect(host.calls.find((c) => c.name === "settings.saveOnlineKey")?.args).toEqual({ key: GOOD_KEY });
    expect(screen.getByText("Key saved. It works.")).toBeTruthy();
    expect(row("api-key").getByText("AIza ···· 9z8y")).toBeTruthy();
    expect(row("api-key").getByText("Works")).toBeTruthy();
    expect(row("api-key").getByRole("button", { name: "Change Gemini API key" })).toBe(document.activeElement);
  });

  it("keeps the field and says 'That key didn't work · Try another' when Gemini refuses it", async () => {
    await openSettings({ key: "works" });

    fireEvent.click(row("api-key").getByRole("button", { name: "Change Gemini API key" }));
    await settle();
    await type("Gemini API key", "AIzaTypo");
    fireEvent.click(row("api-key").getByRole("button", { name: "Save" }));
    await wait(60);
    await settle();

    expect(row("api-key").getByRole("status").textContent).toBe("That key didn't work · Try another");
    expect((screen.getByLabelText("Gemini API key") as HTMLInputElement).value).toBe("AIzaTypo");
    expect(screen.getByLabelText("Gemini API key").getAttribute("aria-invalid")).toBe("true");

    // Typing again clears the complaint, and Cancel keeps the working key.
    await type("Gemini API key", "AIzaTypo2");
    expect(row("api-key").queryByText("That key didn't work · Try another")).toBeNull();
    fireEvent.click(row("api-key").getByRole("button", { name: "Cancel" }));
    await settle();
    expect(row("api-key").getByText("AIza ···· 3f9a")).toBeTruthy();
  });

  it("saves a key that couldn't be checked, and says it will be checked on the next Dictation", async () => {
    await openSettings({ key: "none" });

    fireEvent.click(row("api-key").getByRole("button", { name: "Add Gemini API key" }));
    await settle();
    await type("Gemini API key", `offline-${GOOD_KEY}`);
    fireEvent.keyDown(screen.getByLabelText("Gemini API key"), { key: "Enter" });
    await wait(60);
    await settle();

    expect(screen.getByText(/There's no internet to check it/)).toBeTruthy();
    expect(row("api-key").getByText("Saved")).toBeTruthy();
  });

  it("does not send an empty key", async () => {
    const host = await openSettings({ key: "none" });

    fireEvent.click(row("api-key").getByRole("button", { name: "Add Gemini API key" }));
    await settle();

    expect((row("api-key").getByRole("button", { name: "Save" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.keyDown(screen.getByLabelText("Gemini API key"), { key: "Enter" });
    await settle();
    expect(host.calls.some((c) => c.name === "settings.saveOnlineKey")).toBe(false);
  });

  it("Esc cancels the edit and leaves the key as it was", async () => {
    const host = await openSettings({ key: "works" });

    fireEvent.click(row("api-key").getByRole("button", { name: "Change Gemini API key" }));
    await settle();
    fireEvent.keyDown(screen.getByLabelText("Gemini API key"), { key: "Escape" });
    await settle();

    expect(host.calls.some((c) => c.name === "settings.saveOnlineKey")).toBe(false);
    expect(row("api-key").getByText("AIza ···· 3f9a")).toBeTruthy();
  });

  it("removes the key only after the dialog", async () => {
    const host = await openSettings({ key: "works" });

    fireEvent.click(row("api-key").getByRole("button", { name: "Remove Gemini API key" }));
    await settle();
    expect(host.calls.some((c) => c.name === "settings.removeOnlineKey")).toBe(false);
    fireEvent.click(within(screen.getByRole("dialog", { name: "Remove the Gemini key?" })).getByRole("button", { name: "Remove key" }));
    await settle();
    await settle();

    expect(host.calls.some((c) => c.name === "settings.removeOnlineKey")).toBe(true);
    expect(row("api-key").getByText("Not set")).toBeTruthy();
  });

  it("opens the editor when the Overlay's 'Add an API key' lands on the row", async () => {
    const host = createMockHost();
    installSettingsMock(host);
    installTranscriptionMock(host, { key: "none" });
    const bridge = createBridge(host.transport);
    const ready = await bridge.ready();
    render(<Hub bridge={bridge} snapshot={ready} start={{ screen: "settings", row: "api-key" }} />);
    await settle();
    await settle();

    expect(screen.getByLabelText("Gemini API key")).toBeTruthy();
  });
});

describe("Settings, Offline models", () => {
  it("starts English only, with the packs and their measured sizes in the menu", async () => {
    await openSettings();

    const button = row("offline-models").getByRole("button", { name: /^Offline language pack:/ });
    expect(button.textContent).toBe("English only");
    fireEvent.click(button);
    await settle();

    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual([
      "English only",
      "European languages · 25 languages · 639 MB",
      "All languages · about 99 languages · 989 MB · slower",
    ]);
    expect(screen.getByRole("menuitem", { name: "English only" }).getAttribute("aria-current")).toBe("true");
  });

  it("downloads a picked pack with progress and Cancel, and installs it", async () => {
    const host = await openSettings({ stepMs: 15 });

    fireEvent.click(row("offline-models").getByRole("button", { name: /^Offline language pack:/ }));
    await settle();
    fireEvent.click(screen.getByRole("menuitem", { name: /^European languages/ }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.pickOfflinePack")?.args).toEqual({ pack: "european" });
    expect(row("offline-models").getByRole("progressbar", { name: "Downloading European languages" })).toBeTruthy();
    expect(row("offline-models").getByRole("button", { name: "Cancel European languages" })).toBe(document.activeElement);
    expect(row("offline-models").getByText(/^European languages · \d+ of 639 MB/)).toBeTruthy();

    await wait(300);
    await settle();

    expect(screen.getByText("European languages installed. Offline now uses it.")).toBeTruthy();
    const button = row("offline-models").getByRole("button", { name: "Offline language pack: European languages" });
    expect(button).toBe(document.activeElement);
    expect(row("offline-models").getByText("European languages installed · 25 languages. Picking another pack replaces it.")).toBeTruthy();
  });

  it("Cancel stops the download and goes back to what was installed", async () => {
    const host = await openSettings({ stepMs: 1000, pack: "european" });

    fireEvent.click(row("offline-models").getByRole("button", { name: /^Offline language pack:/ }));
    await settle();
    fireEvent.click(screen.getByRole("menuitem", { name: /^All languages/ }));
    await settle();
    fireEvent.click(row("offline-models").getByRole("button", { name: "Cancel All languages" }));
    await settle();

    expect(host.calls.some((c) => c.name === "settings.cancelOfflinePack")).toBe(true);
    expect(row("offline-models").queryByRole("progressbar")).toBeNull();
    expect(row("offline-models").getByRole("button", { name: "Offline language pack: European languages" })).toBeTruthy();
  });

  it("picking English only sends 'none' to remove the extra pack", async () => {
    const host = await openSettings({ pack: "allLanguages" });

    fireEvent.click(row("offline-models").getByRole("button", { name: /^Offline language pack:/ }));
    await settle();
    fireEvent.click(screen.getByRole("menuitem", { name: "English only" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.pickOfflinePack")?.args).toEqual({ pack: "none" });
    expect(row("offline-models").getByRole("button", { name: "Offline language pack: English only" })).toBeTruthy();
  });

  it("shows a download in progress on arrival, with its percentage", async () => {
    await openSettings({ download: { pack: "european", fraction: 0.4 } });

    const bar = row("offline-models").getByRole("progressbar", { name: "Downloading European languages" });
    expect(bar.getAttribute("aria-valuenow")).toBe("40");
    expect(row("offline-models").getByText("40%")).toBeTruthy();
  });

  it("says a stopped download stopped, and Try again picks the same pack", async () => {
    const host = await openSettings({ download: { pack: "allLanguages", fraction: 0.3, failed: true }, stepMs: 1000 });

    expect(row("offline-models").getByRole("status").textContent).toBe(
      "The download stopped. Check your internet connection, then try again.",
    );
    fireEvent.click(row("offline-models").getByRole("button", { name: "Try All languages again" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.pickOfflinePack")?.args).toEqual({ pack: "allLanguages" });
    expect(row("offline-models").getByRole("progressbar", { name: "Downloading All languages" })).toBeTruthy();
  });
});

describe("Settings, Smart cleanup", () => {
  it("reads Off while no model is on disk, and says nothing is downloaded yet", async () => {
    await openSettings();

    expect(row("smart-cleanup").getByRole("switch", { name: "Smart cleanup" }).getAttribute("aria-checked")).toBe("false");
    expect(row("smart-cleanup-model").getByRole("status").textContent).toBe(
      "Not downloaded yet, so Smart cleanup is off. Turn it on to download it; Offline uses its plain cleanup until then.",
    );
    expect(row("smart-cleanup-model").getByRole("button", { name: "Download" })).toBeTruthy();
  });

  it("offers the two models with their sizes, Better chosen", async () => {
    await openSettings();

    const labels = row("smart-cleanup-model").getAllByRole("radio").map((r) => r.textContent);
    expect(labels).toEqual(["Standard · 1.0 GB", "Better · 2.3 GB"]);
    expect(row("smart-cleanup-model").getByRole("radio", { name: "Better · 2.3 GB" }).getAttribute("aria-checked")).toBe("true");
  });

  it("saves the switch through the host", async () => {
    const host = await openSettings({ smart: { installed: "better" } });

    fireEvent.click(row("smart-cleanup").getByRole("switch", { name: "Smart cleanup" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.setSmartCleanup")?.args).toEqual({ on: false });
    expect(row("smart-cleanup").getByRole("switch", { name: "Smart cleanup" }).getAttribute("aria-checked")).toBe("false");
  });

  it("downloads the chosen model with progress and Cancel, then says it is ready", async () => {
    const host = await openSettings({ stepMs: 30 });

    fireEvent.click(row("smart-cleanup-model").getByRole("button", { name: "Download" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.pickSmartCleanup")?.args).toEqual({ tier: "better" });
    expect(row("smart-cleanup-model").getByRole("progressbar", { name: "Downloading the Better model" })).toBeTruthy();
    expect(row("smart-cleanup-model").getByRole("button", { name: "Cancel Better" })).toBeTruthy();

    await wait(600);
    await settle();
    expect(row("smart-cleanup-model").getByText("Better model installed: closer to what Online writes, about 5 seconds more per Dictation.")).toBeTruthy();
    expect(row("smart-cleanup-model").getByRole("button", { name: "Remove" })).toBeTruthy();
  });

  it("Cancel stops the download", async () => {
    const host = await openSettings({ smart: { installed: null, download: { tier: "better", fraction: 0.4 } }, stepMs: 1000 });

    fireEvent.click(row("smart-cleanup-model").getByRole("button", { name: "Cancel Better" }));
    await settle();

    expect(host.calls.some((c) => c.name === "settings.cancelSmartCleanup")).toBe(true);
    expect(row("smart-cleanup-model").getByRole("button", { name: "Download" })).toBeTruthy();
  });

  it("says a stopped download stopped, and Try again picks the same model", async () => {
    const host = await openSettings({ smart: { installed: null, download: { tier: "standard", fraction: 0.3, failed: true } }, stepMs: 1000 });

    expect(row("smart-cleanup-model").getByRole("status").textContent).toBe("The download stopped. Check your internet connection, then try again.");
    fireEvent.click(row("smart-cleanup-model").getByRole("button", { name: "Try Standard again" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.pickSmartCleanup")?.args).toEqual({ tier: "standard" });
  });

  it("switching models downloads the other one while the installed one keeps running", async () => {
    const host = await openSettings({ smart: { installed: "standard" }, stepMs: 1000 });

    fireEvent.click(row("smart-cleanup-model").getByRole("radio", { name: "Better · 2.3 GB" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.pickSmartCleanup")?.args).toEqual({ tier: "better" });
    expect(row("smart-cleanup-model").getByRole("progressbar", { name: "Downloading the Better model" })).toBeTruthy();
  });

  it("Remove asks first, then deletes the model and turns Smart cleanup off", async () => {
    const host = await openSettings({ smart: { installed: "better" } });

    fireEvent.click(row("smart-cleanup-model").getByRole("button", { name: "Remove" }));
    await settle();
    expect(host.calls.some((c) => c.name === "settings.removeSmartCleanup")).toBe(false);
    fireEvent.click(within(screen.getByRole("dialog", { name: "Remove the Smart cleanup model?" })).getByRole("button", { name: "Remove" }));
    await settle();
    await settle();

    expect(host.calls.some((c) => c.name === "settings.removeSmartCleanup")).toBe(true);
    expect(row("smart-cleanup").getByRole("switch", { name: "Smart cleanup" }).getAttribute("aria-checked")).toBe("false");
  });
});
