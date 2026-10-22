import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import { createMockHost } from "../src/bridge/mock";
import type { MockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installSettingsMock } from "../src/hub/mockSettings";
import type { SettingsScenario } from "../src/hub/mockSettings";
import { rowDomId } from "../src/hub/screens";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

async function openSettings(scenario: SettingsScenario = {}): Promise<MockHost> {
  const host = createMockHost();
  installSettingsMock(host, scenario);
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

/** Presses keys as the mock's page listener hears them: all down, then all up in reverse. */
async function press(...codes: { code: string; key: string }[]) {
  for (const k of codes) fireEvent.keyDown(window, k);
  for (const k of [...codes].reverse()) fireEvent.keyUp(window, k);
  await settle();
}

const F8 = { code: "F8", key: "F8" };
const A = { code: "KeyA", key: "a" };
const AltLeft = { code: "AltLeft", key: "Alt" };
const Space = { code: "Space", key: " " };
const Escape = { code: "Escape", key: "Escape" };

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Settings, Microphone", () => {
  it("names the chosen microphone and switches to another from the menu", async () => {
    const host = await openSettings();

    const button = row("microphone").getByRole("button", { name: /^Microphone:/ });
    expect(button.textContent).toBe("Headset (Jabra Evolve2 65)");

    fireEvent.click(button);
    await settle();
    fireEvent.click(screen.getByRole("menuitem", { name: "USB Microphone (Yeti)" }));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.setMicrophone")?.args).toEqual({ id: "{yeti}" });
    expect(row("microphone").getByRole("button", { name: /^Microphone:/ }).textContent).toBe("USB Microphone (Yeti)");
  });

  it("lists the Windows default once, with the device it stands for, and ticks the choice", async () => {
    await openSettings();

    fireEvent.click(row("microphone").getByRole("button", { name: /^Microphone:/ }));
    await settle();
    const items = screen.getAllByRole("menuitem").map((item) => item.textContent);

    expect(items).toEqual(["Windows default · Microphone (Realtek Audio)", "Headset (Jabra Evolve2 65)", "USB Microphone (Yeti)"]);
    expect(screen.getByRole("menuitem", { name: "Headset (Jabra Evolve2 65)" }).getAttribute("aria-current")).toBe("true");
  });

  it("says when the chosen microphone is unplugged and the default is used", async () => {
    await openSettings({ mics: "unplugged" });

    expect(row("microphone").getByRole("status").textContent).toMatch(/Not connected/);
    expect(row("microphone").getByRole("button", { name: /^Microphone:/ }).textContent).toBe("USB Microphone (Yeti)");
  });

  it("opens the menu when a Quick Find or status-card landing arrives on the row", async () => {
    await openSettings();

    fireEvent.change(screen.getByRole("combobox", { name: "Quick Find" }), { target: { value: "microphone" } });
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Quick Find" }), { key: "Enter" });
    await settle();
    await settle();

    expect(screen.getByRole("menu", { name: "Microphone" })).toBeTruthy();
  });

  it("shows no menu when Windows has no microphone", async () => {
    await openSettings({ mics: "none" });

    const button = row("microphone").getByRole("button", { name: "No microphone found" }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  it("runs the level only while the row is on screen, and draws it on the bars", async () => {
    let report: ((entries: { isIntersecting: boolean }[]) => void) | undefined;
    vi.stubGlobal(
      "IntersectionObserver",
      class {
        constructor(callback: (entries: { isIntersecting: boolean }[]) => void) {
          report = callback;
        }
        observe() {}
        disconnect() {}
      },
    );
    const host = await openSettings({ level: 1 });

    act(() => report?.([{ isIntersecting: true }]));
    await settle();
    const bars = document.querySelectorAll<HTMLElement>(".hub-mic-level i");
    expect(host.calls.filter((c) => c.name === "mic.preview").map((c) => c.args)).toEqual([{ on: true }]);
    expect(bars[1]?.style.transform).toBe("scaleY(1.000)");

    act(() => report?.([{ isIntersecting: false }]));
    await settle();
    expect(host.calls.filter((c) => c.name === "mic.preview").map((c) => c.args)).toEqual([{ on: true }, { on: false }]);
  });
});

describe("Settings, keys", () => {
  it("records a new Hotkey through the host", async () => {
    const host = await openSettings();

    fireEvent.click(row("hotkey").getByRole("button", { name: "Change Hotkey" }));
    await settle();
    expect(row("hotkey").getByRole("status").textContent).toBe("Press a key");
    await press(F8);

    expect(host.calls.find((c) => c.name === "settings.captureKey")?.args).toEqual({ target: "hotkey" });
    expect(row("hotkey").getByLabelText("F8")).toBeTruthy();
    expect(row("hotkey").getByRole("button", { name: "Change Hotkey" })).toBe(document.activeElement);
  });

  it("shows why a key can't be used in place of the description, until a good one", async () => {
    await openSettings();

    fireEvent.click(row("hotkey").getByRole("button", { name: "Change Hotkey" }));
    await settle();
    await press(A);

    expect(row("hotkey").getByText(/Choose a key you don't type with/)).toBeTruthy();
    await press(F8);
    expect(row("hotkey").queryByText(/Choose a key you don't type with/)).toBeNull();
    expect(row("hotkey").getByText("Double-tap it to show or hide the Overlay.")).toBeTruthy();
  });

  it("Cancel and Esc leave the key as it was", async () => {
    const host = await openSettings();

    fireEvent.click(row("hotkey").getByRole("button", { name: "Change Hotkey" }));
    await settle();
    fireEvent.click(row("hotkey").getByRole("button", { name: "Cancel" }));
    await settle();
    expect(host.calls.some((c) => c.name === "settings.cancelCapture")).toBe(true);
    expect(row("hotkey").getByLabelText("Ctrl")).toBeTruthy();

    fireEvent.click(row("hotkey").getByRole("button", { name: "Change Hotkey" }));
    await settle();
    await press(Escape);
    expect(row("hotkey").getByLabelText("Ctrl")).toBeTruthy();
  });

  it("starts with Left Ctrl + Left Win on the Trigger Key and Left Ctrl + Left Shift on the Rewrite key", async () => {
    await openSettings();

    expect(row("trigger-key").getByLabelText("Left Ctrl + Left Win")).toBeTruthy();
    expect(row("rewrite-key").getByLabelText("Left Ctrl + Left Shift")).toBeTruthy();
  });

  it("removes the Trigger Key, then sets up a chord again", async () => {
    const host = await openSettings();

    fireEvent.click(row("trigger-key").getByRole("button", { name: "Remove Trigger Key" }));
    await settle();
    expect(host.calls.some((c) => c.name === "settings.clearTriggerKey")).toBe(true);
    expect(row("trigger-key").getByText("Not set")).toBeTruthy();

    fireEvent.click(row("trigger-key").getByRole("button", { name: "Set up Trigger Key" }));
    await settle();
    await press(AltLeft, Space);
    expect(row("trigger-key").getByLabelText("Left Alt + Space")).toBeTruthy();
  });

  it("shows the Rewrite key as Left Ctrl + Left Shift, changes it and removes it", async () => {
    const host = await openSettings();

    expect(row("rewrite-key").getByLabelText("Left Ctrl + Left Shift")).toBeTruthy();
    fireEvent.click(row("rewrite-key").getByRole("button", { name: "Change Rewrite key" }));
    await settle();
    await press(AltLeft, Space);
    expect(row("rewrite-key").getByLabelText("Left Alt + Space")).toBeTruthy();

    fireEvent.click(row("rewrite-key").getByRole("button", { name: "Remove Rewrite key" }));
    await settle();
    expect(host.calls.some((c) => c.name === "settings.clearRewriteKey")).toBe(true);
    expect(row("rewrite-key").getByText("Not set")).toBeTruthy();
  });

  it("only one row listens at a time", async () => {
    await openSettings();

    fireEvent.click(row("hotkey").getByRole("button", { name: "Change Hotkey" }));
    await settle();
    fireEvent.click(row("paste-again").getByRole("button", { name: "Change Paste Again" }));
    await settle();

    expect(row("hotkey").queryByRole("status")).toBeNull();
    expect(row("paste-again").getByRole("status").textContent).toBe("Press the keys");
  });

  it("says when another app holds the Paste Again keys", async () => {
    await openSettings({ pasteAgainTaken: true });

    expect(row("paste-again").getByRole("status").textContent).toBe(
      "Another app is using Ctrl + Shift + V, so Paste Again can't work. Choose other keys.",
    );
  });

  it("toasts a known Windows behaviour the new key may set off", async () => {
    const host = await openSettings();
    host.handle("settings.captureKey", () => ({ saved: true, warning: "May trigger Windows' Sticky Keys prompt." }));

    fireEvent.click(row("hotkey").getByRole("button", { name: "Change Hotkey" }));
    await settle();

    expect(screen.getByText("Saved. May trigger Windows' Sticky Keys prompt.")).toBeTruthy();
  });
});

describe("Settings, Press Enter and Mute Other Audio", () => {
  it("starts with Press Enter off and Mute Other Audio on", async () => {
    await openSettings();

    expect(row("press-enter").getByRole("switch", { name: "Press Enter" }).getAttribute("aria-checked")).toBe("false");
    expect(row("mute-other-audio").getByRole("switch", { name: "Mute Other Audio" }).getAttribute("aria-checked")).toBe("true");
  });

  it("saves each switch through the host", async () => {
    const host = await openSettings();

    fireEvent.click(row("press-enter").getByRole("switch", { name: "Press Enter" }));
    await settle();
    fireEvent.click(row("mute-other-audio").getByRole("switch", { name: "Mute Other Audio" }));
    await settle();

    expect(host.calls.filter((c) => c.name.startsWith("settings.set")).map((c) => [c.name, c.args])).toEqual([
      ["settings.setPressEnter", { on: true }],
      ["settings.setMuteOtherAudio", { on: false }],
    ]);
    expect(row("press-enter").getByRole("switch").getAttribute("aria-checked")).toBe("true");
    expect(row("mute-other-audio").getByRole("switch").getAttribute("aria-checked")).toBe("false");
  });
});
