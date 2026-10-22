import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { UpdateView } from "../src/bridge/contracts.g";
import { createBridge } from "../src/bridge/bridge";
import { createMockHost } from "../src/bridge/mock";
import type { MockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installGeneralMock } from "../src/hub/mockGeneral";
import type { GeneralScenario } from "../src/hub/mockGeneral";
import { installSettingsMock } from "../src/hub/mockSettings";
import { installTranscriptionMock } from "../src/hub/mockTranscription";
import { rowDomId } from "../src/hub/screens";
import { updateLine } from "../src/hub/settings/updates";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

const wait = (ms: number) => act(() => new Promise((resolve) => setTimeout(resolve, ms)));

async function openSettings(scenario: GeneralScenario = {}): Promise<MockHost> {
  const host = createMockHost();
  installSettingsMock(host);
  installTranscriptionMock(host);
  installGeneralMock(host, { stepMs: 5, ...scenario });
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

const lastCall = (host: MockHost, name: string) => host.calls.filter((c) => c.name === name).at(-1)?.args;

afterEach(() => vi.restoreAllMocks());

describe("the Automatic updates line", () => {
  const now = new Date(2026, 8, 30, 9, 50).getTime();
  const base: UpdateView = {
    state: "upToDate",
    automatic: true,
    currentVersion: "4.0.0",
    newVersion: null,
    progress: 0,
    lastChecked: new Date(2026, 8, 30, 9, 41).getTime(),
    checkFailed: false,
  };

  it("says each state as DESIGN §12 words it", () => {
    expect(updateLine(base, now, "en-GB")).toBe("Up to date. Checked today at 09:41.");
    expect(updateLine({ ...base, state: "downloading", newVersion: "4.1" }, now)).toBe("Downloading 4.1 in the background.");
    expect(updateLine({ ...base, state: "ready", newVersion: "4.1" }, now)).toBe("4.1 is ready. It installs next time VoiceCtrl starts.");
    expect(updateLine({ ...base, state: "off", automatic: false }, now)).toBe("Off. You stay on 4.0.0 until you check.");
    expect(updateLine({ ...base, state: "available", newVersion: "4.1" }, now)).toBe("4.1 is out. You're on 4.0.0.");
  });

  it("names an older check by its day, and a failed check without blame", () => {
    const yesterday = new Date(2026, 8, 29, 18, 5).getTime();
    expect(updateLine({ ...base, lastChecked: yesterday }, now, "en-GB")).toBe("Up to date. Checked yesterday at 18:05.");
    expect(updateLine({ ...base, checkFailed: true }, now, "en-GB")).toBe("Couldn't reach GitHub. Last checked today at 09:41.");
    expect(updateLine({ ...base, checkFailed: true, lastChecked: null }, now)).toBe("Couldn't reach GitHub. VoiceCtrl tries again later.");
  });
});

describe("Settings, Sound and Overlay", () => {
  it("turns Sound Cues on and plays them on request", async () => {
    const host = await openSettings();

    fireEvent.click(row("sound-cues").getByRole("button", { name: "Play" }));
    fireEvent.click(row("sound-cues").getByRole("switch", { name: "Sound Cues" }));
    await settle();

    expect(host.calls.some((c) => c.name === "settings.playSoundCues")).toBe(true);
    expect(lastCall(host, "settings.setSoundCues")).toEqual({ on: true });
    expect(row("sound-cues").getByRole("switch").getAttribute("aria-checked")).toBe("true");
  });

  it("moves the Overlay and follows a drag made elsewhere", async () => {
    const host = await openSettings();

    fireEvent.click(row("overlay-position").getByRole("radio", { name: "Left" }));
    await settle();
    expect(lastCall(host, "settings.setOverlayDock")).toEqual({ dock: "left" });

    // Dragging the Overlay writes the dock, and the host pushes the section.
    host.emit("settings.general", {
      general: {
        soundCues: false,
        overlayDock: "right",
        alwaysShowOverlay: false,
        keepDictations: "last50",
        startWithWindows: true,
        updates: { state: "upToDate", automatic: true, currentVersion: "4.0.0", newVersion: null, progress: 0, lastChecked: null, checkFailed: false },
        engineRecord: false,
      },
    });
    await settle();
    expect(row("overlay-position").getByRole("radio", { name: "Right" }).getAttribute("aria-checked")).toBe("true");
  });

  it("switches Always show the Overlay", async () => {
    const host = await openSettings();

    fireEvent.click(row("always-show-overlay").getByRole("switch", { name: "Always show the Overlay" }));
    await settle();

    expect(lastCall(host, "settings.setAlwaysShowOverlay")).toEqual({ on: true });
  });
});

describe("Settings, Privacy", () => {
  it("asks before a shorter Keep Dictations removes kept ones, and saves nothing on Cancel", async () => {
    const host = await openSettings({ removes: 12 });

    fireEvent.click(row("keep-dictations").getByRole("radio", { name: "None" }));
    await settle();

    const dialog = screen.getByRole("dialog", { name: "Stop keeping Dictations?" });
    expect(dialog.textContent).toContain("The 12 Dictations kept on this PC are removed now.");
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await settle();

    expect(lastCall(host, "settings.setKeepDictations")).toBeUndefined();
    expect(row("keep-dictations").getByRole("radio", { name: "Last 50" }).getAttribute("aria-checked")).toBe("true");
  });

  it("removes them once confirmed", async () => {
    const host = await openSettings({ removes: 7 });

    fireEvent.click(row("keep-dictations").getByRole("radio", { name: "24 hours" }));
    await settle();
    const dialog = screen.getByRole("dialog", { name: "Keep only the last 24 hours?" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Remove 7" }));
    await settle();

    expect(lastCall(host, "settings.setKeepDictations")).toEqual({ keep: "last24Hours" });
  });

  it("saves a longer choice straight away", async () => {
    const host = await openSettings({ keep: "none" });

    fireEvent.click(row("keep-dictations").getByRole("radio", { name: "Last 50" }));
    await settle();

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(lastCall(host, "settings.setKeepDictations")).toEqual({ keep: "last50" });
  });

  it("opens the data folder", async () => {
    const host = await openSettings();

    fireEvent.click(row("your-data").getByRole("button", { name: "Open folder" }));
    await settle();

    expect(host.calls.some((c) => c.name === "settings.openDataFolder")).toBe(true);
  });

  it("copies diagnostics, says Copied, and offers GitHub's issues page", async () => {
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    await openSettings();

    fireEvent.click(row("copy-diagnostics").getByRole("button", { name: "Copy" }));
    await settle();

    expect(row("copy-diagnostics").getByRole("button", { name: "Copied" })).toBeTruthy();
    expect(screen.getByText("Diagnostics copied. Paste them into a GitHub issue.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Open GitHub" }));
    expect(open).toHaveBeenCalledWith("https://github.com/aadershk/VoiceCtrl/issues/new", "_blank", "noopener");
  });

  it("says so when the clipboard is busy", async () => {
    await openSettings({ clipboardBusy: true });

    fireEvent.click(row("copy-diagnostics").getByRole("button", { name: "Copy" }));
    await settle();

    expect(screen.getByText("Couldn't copy. Another app is using the clipboard, so try again.")).toBeTruthy();
    expect(row("copy-diagnostics").getByRole("button", { name: "Copy" })).toBeTruthy();
  });
});

describe("Settings, Engine record", () => {
  it("is off until it is switched on, and says what it keeps and where", async () => {
    await openSettings();

    const toggle = row("engine-record").getByRole("switch", { name: "Engine record" });
    expect(toggle.getAttribute("aria-checked")).toBe("false");
    const text = document.getElementById(rowDomId("engine-record"))?.textContent ?? "";
    expect(text).toContain("never what you said");
    expect(text).toContain("this PC");
    expect(text).toContain("deletes it");
  });

  it("switches on and off, and follows the host", async () => {
    const host = await openSettings();
    const toggle = () => row("engine-record").getByRole("switch", { name: "Engine record" });

    fireEvent.click(toggle());
    await settle();
    expect(lastCall(host, "settings.setEngineRecord")).toEqual({ on: true });
    expect(toggle().getAttribute("aria-checked")).toBe("true");

    fireEvent.click(toggle());
    await settle();
    expect(lastCall(host, "settings.setEngineRecord")).toEqual({ on: false });
    expect(toggle().getAttribute("aria-checked")).toBe("false");
  });

  it("starts on when the host says it is on", async () => {
    await openSettings({ engineRecord: true });

    expect(row("engine-record").getByRole("switch", { name: "Engine record" }).getAttribute("aria-checked")).toBe("true");
  });
});

describe("Settings, App", () => {
  it("turns Start VoiceCtrl with Windows off", async () => {
    const host = await openSettings();

    const toggle = row("start-with-windows").getByRole("switch", { name: "Start VoiceCtrl with Windows" });
    expect(toggle.getAttribute("aria-checked")).toBe("true");
    fireEvent.click(toggle);
    await settle();

    expect(lastCall(host, "settings.setStartWithWindows")).toEqual({ on: false });
  });

  it("checks, downloads and offers Restart now", async () => {
    // Slow enough steps that "Checking" is still up when it is read, even on a busy CI runner.
    const host = await openSettings({ stepMs: 150 });

    fireEvent.click(row("automatic-updates").getByRole("button", { name: "Check now" }));
    await settle();
    expect(row("automatic-updates").getByText("Checking for a new version.")).toBeTruthy();

    // One step to check, four to download.
    await wait(150 * 5 + 250);
    await settle();
    expect(row("automatic-updates").getByText("4.1 is ready. It installs next time VoiceCtrl starts.")).toBeTruthy();
    fireEvent.click(row("automatic-updates").getByRole("button", { name: "Restart now" }));
    expect(host.calls.some((c) => c.name === "updates.restart")).toBe(true);
  });

  it("shows a download's progress", async () => {
    await openSettings({ update: { state: "downloading", progress: 0.62 } });

    expect(row("automatic-updates").getByText("62%")).toBeTruthy();
    expect(row("automatic-updates").getByRole("progressbar").getAttribute("aria-valuenow")).toBe("62");
  });

  it("turns off, and then a check offers the version instead of downloading it", async () => {
    const host = await openSettings();

    fireEvent.click(row("automatic-updates").getByRole("switch", { name: "Automatic updates" }));
    await settle();
    expect(lastCall(host, "updates.setAutomatic")).toEqual({ on: false });
    expect(row("automatic-updates").getByText("Off. You stay on 4.0.0 until you check.")).toBeTruthy();

    fireEvent.click(row("automatic-updates").getByRole("button", { name: "Check now" }));
    await wait(20);
    await settle();
    fireEvent.click(row("automatic-updates").getByRole("button", { name: "Download and install" }));
    await settle();

    expect(host.calls.some((c) => c.name === "updates.download")).toBe(true);
  });

  it("closes with the About card and its links", async () => {
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    await openSettings();

    const about = within(screen.getByRole("region", { name: "About VoiceCtrl" }));
    expect(about.getByText("VoiceCtrl 4.0.0")).toBeTruthy();
    fireEvent.click(about.getByRole("button", { name: "What's new" }));
    fireEvent.click(about.getByRole("button", { name: "GitHub" }));

    expect(open.mock.calls.map((c) => c[0])).toEqual([
      "https://github.com/aadershk/VoiceCtrl/releases/tag/v4.0.0",
      "https://github.com/aadershk/VoiceCtrl",
    ]);
  });
});
