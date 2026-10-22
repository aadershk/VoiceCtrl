import { act, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import type { AppStatus, TrayOpening } from "../src/bridge/contracts.g";
import { createMockHost, defaultSnapshot } from "../src/bridge/mock";
import { Flyout } from "../src/flyout/Flyout";
import { installFlyoutMock } from "../src/flyout/mockFlyout";
import { flyoutStatusLine } from "../src/flyout/statusLine";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

const status = (patch: Partial<AppStatus>): AppStatus => ({ ...defaultSnapshot.status, ...patch });
const opening = (patch: Partial<TrayOpening> = {}): TrayOpening => ({
  corner: "bottomRight",
  mode: "auto",
  hasLatest: true,
  ...patch,
});

async function renderFlyout(snapshot: Parameters<typeof createMockHost>[0] = {}) {
  const host = createMockHost(snapshot);
  installFlyoutMock(host);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Flyout bridge={bridge} snapshot={ready} />);
  const open = async (patch: Partial<TrayOpening> = {}) => {
    host.emit("tray.open", opening(patch));
    await settle();
  };
  const sent = () => host.calls.map((c) => c.name).filter((name) => name !== "app.ready");
  return { host, open, sent };
}

const item = (name: string) => screen.getByRole("menuitem", { name });
const mode = (name: string) => screen.getByRole("menuitemradio", { name });

describe("The flyout's status line", () => {
  it("says how to show the bar when ready, with the user's Hotkey", () => {
    expect(flyoutStatusLine(status({ kind: "ready", hotkey: "Right Alt" }))).toBe("Ready · Double-tap Right Alt to show the bar");
  });

  it("says to hold the talk keys when the Trigger Key is on", () => {
    expect(flyoutStatusLine(status({ kind: "ready", talkKeys: ["Left Ctrl", "Left Win"] }))).toBe("Ready · Hold Left Ctrl + Left Win to talk");
    expect(flyoutStatusLine(status({ kind: "paused", paused: true, talkKeys: ["Left Ctrl", "Left Win"] }))).toBe("Paused · Your hotkeys do nothing");
  });

  it("says the double-tap does nothing while paused", () => {
    expect(flyoutStatusLine(status({ kind: "paused", paused: true }))).toBe("Paused · The Ctrl double-tap does nothing");
  });

  it("reads a warning as the status card's title and fix", () => {
    expect(flyoutStatusLine(status({ kind: "noMicrophone" }))).toBe("No microphone · Choose one in Settings");
    expect(flyoutStatusLine(status({ kind: "apiKeyNotWorking", offlineFallback: true }))).toBe(
      "Gemini key not working · Using Offline until it's fixed",
    );
  });
});

describe("Tray flyout", () => {
  it("renders nothing until the host opens it", async () => {
    await renderFlyout();

    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("opens with the menu focused, no item picked out, and tells the host its size", async () => {
    const { open, host } = await renderFlyout();

    await open();

    expect(document.activeElement).toBe(screen.getByRole("menu", { name: "VoiceCtrl" }));
    expect(host.calls.some((c) => c.name === "tray.place")).toBe(true);
  });

  it("shows the status line, Pause and the current Mode", async () => {
    const { open } = await renderFlyout();

    await open({ mode: "offline" });

    expect(screen.getByText("Ready · Double-tap Ctrl to show the bar")).toBeTruthy();
    expect(item("Pause")).toBeTruthy();
    expect(mode("Offline").getAttribute("aria-checked")).toBe("true");
    expect(mode("Auto").getAttribute("aria-checked")).toBe("false");
  });

  it("disables Copy latest Transcription when there is none", async () => {
    const { open } = await renderFlyout();

    await open({ hasLatest: false });

    expect((item("Copy latest Transcription") as HTMLButtonElement).disabled).toBe(true);
  });

  it("closes on a pick, then does it", async () => {
    const { open, sent } = await renderFlyout();
    await open();

    fireEvent.click(item("Settings"));
    await settle();

    expect(sent()).toEqual(["tray.place", "tray.close", "tray.openSettings"]);
  });

  it("pauses through the shared app.setPaused, and offers Resume while paused", async () => {
    const { open, host } = await renderFlyout();
    await open();

    fireEvent.click(item("Pause"));
    await settle();
    expect(host.calls.find((c) => c.name === "app.setPaused")?.args).toEqual({ paused: true });

    await open();
    expect(item("Resume")).toBeTruthy();
    expect(screen.getByText("Paused · The Ctrl double-tap does nothing")).toBeTruthy();
  });

  it("changes Mode without closing", async () => {
    const { open, host, sent } = await renderFlyout();
    await open();

    fireEvent.click(mode("Online"));
    await settle();

    expect(host.calls.find((c) => c.name === "settings.setMode")?.args).toEqual({ mode: "online" });
    expect(mode("Online").getAttribute("aria-checked")).toBe("true");
    expect(sent()).not.toContain("tray.close");
  });

  it("each opening brings fresh state and starts on the menu again", async () => {
    const { open } = await renderFlyout();
    await open({ hasLatest: false, mode: "auto" });
    item("Quit VoiceCtrl").focus();

    await open({ hasLatest: true, mode: "online" });

    expect((item("Copy latest Transcription") as HTMLButtonElement).disabled).toBe(false);
    expect(mode("Online").getAttribute("aria-checked")).toBe("true");
    expect(document.activeElement).toBe(screen.getByRole("menu"));
  });

  it("moves with the arrow keys through the items and the Mode control", async () => {
    const { open } = await renderFlyout();
    await open({ hasLatest: true });
    const menu = screen.getByRole("menu");

    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(document.activeElement).toBe(item("Open VoiceCtrl"));

    for (let i = 0; i < 3; i += 1) fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(document.activeElement).toBe(mode("Auto"));

    fireEvent.keyDown(document.activeElement as Element, { key: "ArrowRight" });
    expect(document.activeElement).toBe(mode("Online"));

    fireEvent.keyDown(menu, { key: "End" });
    expect(document.activeElement).toBe(item("Quit VoiceCtrl"));
  });

  it("goes to the last item on Up from the menu itself", async () => {
    const { open } = await renderFlyout();
    await open();

    fireEvent.keyDown(screen.getByRole("menu"), { key: "ArrowUp" });

    expect(document.activeElement).toBe(item("Quit VoiceCtrl"));
  });

  it("closes on Esc and on a click outside the menu", async () => {
    const { open, sent } = await renderFlyout();
    await open();

    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    fireEvent.pointerDown(document.body);
    await settle();

    expect(sent().filter((name) => name === "tray.close")).toHaveLength(2);
  });

  it("follows the status while open", async () => {
    const { open, host } = await renderFlyout();
    await open();

    host.emit("app.status", { status: status({ kind: "noMicrophone" }) });
    await settle();

    expect(screen.getByText("No microphone · Choose one in Settings")).toBeTruthy();
  });
});
