import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import { createMockHost } from "../src/bridge/mock";
import type { MockHost } from "../src/bridge/mock";
import { installSetupMock } from "../src/setup/mockSetup";
import type { SetupScenario } from "../src/setup/mockSetup";
import { AI_STUDIO_URL, Setup } from "../src/setup/Setup";

const KEY = "AIzaSyB7Qk2mL0pX4vT9sW1cE8rN6yH3f9a";
const settle = () => act(() => new Promise((resolve) => setTimeout(resolve, 0)));

let open: ReturnType<typeof vi.fn>;
beforeEach(() => {
  open = vi.fn();
  vi.stubGlobal("open", open);
});
afterEach(() => vi.unstubAllGlobals());

async function renderSetup(scenario: SetupScenario = {}): Promise<MockHost> {
  const host = createMockHost();
  installSetupMock(host, { checkDelay: 0, ...scenario });
  const bridge = createBridge(host.transport);
  const app = await bridge.ready();
  const setup = await bridge.call("setup.state", {});
  render(<Setup bridge={bridge} app={app} setup={setup} />);
  return host;
}

const click = (name: string | RegExp) => fireEvent.click(screen.getByRole("button", { name }));
const lastCall = (host: MockHost, name: string) => host.calls.filter((c) => c.name === name).at(-1);

async function toKey() {
  click("Continue");
  await settle();
}

async function submitKey(key = KEY) {
  fireEvent.change(screen.getByLabelText("Paste it here"), { target: { value: key } });
  click("Continue");
  await settle();
  await settle();
  await settle();
}

async function toReady() {
  await toKey();
  click("Skip for now");
  await settle();
  await settle();
}

/** Presses keys as the mock's page listener hears them: all down, then all up in reverse. */
async function press(...codes: { code: string; key: string }[]) {
  for (const k of codes) fireEvent.keyDown(window, k);
  for (const k of [...codes].reverse()) fireEvent.keyUp(window, k);
  await settle();
  await settle();
}

const AltLeft = { code: "AltLeft", key: "Alt" };
const CtrlLeft = { code: "ControlLeft", key: "Control" };
const ShiftLeft = { code: "ShiftLeft", key: "Shift" };
const MetaLeft = { code: "MetaLeft", key: "Meta" };
const Space = { code: "Space", key: " " };
const KeyL = { code: "KeyL", key: "l" };

describe("Setup", () => {
  it("shows the three steps, the current one marked, and the version", async () => {
    await renderSetup();

    const steps = screen.getAllByRole("listitem").map((li) => li.textContent);
    expect(steps).toEqual(["1Choose how it listens", "2Connect your Online provider", "3Learn your hotkey"]);
    expect(screen.getByText("Choose how it listens").getAttribute("aria-current")).toBe("step");
    expect(screen.getByText("Free and open source · 4.0.0")).toBeTruthy();
  });

  it("renames step 2 after the choice", async () => {
    await renderSetup();

    fireEvent.click(screen.getByRole("radio", { name: /Offline/ }));

    expect(screen.getByText("Get the offline model")).toBeTruthy();
  });

  it("opens AI Studio in the browser and says so", async () => {
    await renderSetup();
    await toKey();

    click(/Open AI Studio/);

    expect(open).toHaveBeenCalledWith(AI_STUDIO_URL, "_blank", "noopener");
    expect(screen.getByRole("button", { name: /Opened in your browser/ })).toBeTruthy();
  });

  it("checks the key through the host and moves on by itself when it works", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const host = await renderSetup({ key: "works" });
      await toKey();
      await submitKey();

      expect(lastCall(host, "setup.checkKey")?.args).toEqual({ key: KEY, provider: "gemini", baseUrl: null, model: null });
      expect(screen.getByText("The key works")).toBeTruthy();

      await act(async () => {
        vi.advanceTimersByTime(700);
      });
      expect(screen.getByText("You're all set")).toBeTruthy();
    } finally {
      vi.useRealTimers();
    }
  });

  it("shows the error for a key that didn't work, then uses it anyway", async () => {
    const host = await renderSetup({ key: "didNotWork" });
    await toKey();
    await submitKey("AIza-bad");

    expect(screen.getByRole("alert").textContent).toContain("That key didn't work.");
    expect(screen.getByLabelText("Paste it here").getAttribute("aria-invalid")).toBe("true");

    click("Use it anyway");
    await settle();
    click(/Open VoiceCtrl/);
    await settle();

    expect(lastCall(host, "setup.finish")?.args).toEqual({ listening: "online", apiKey: "AIza-bad", startWithWindows: true, smartCleanup: true, provider: "gemini", baseUrl: null, model: null });
  });

  it("says the key can't be checked with no internet, and never blocks", async () => {
    await renderSetup({ key: "notChecked" });
    await toKey();
    await submitKey();

    expect(screen.getByText(/so the key can't be checked now/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Use it anyway" }).hasAttribute("disabled")).toBe(false);
  });

  it("finishes with no key after Skip, and with Start with Windows as switched", async () => {
    const host = await renderSetup();
    await toKey();
    click("Skip for now");
    await settle();

    expect(screen.getByText(/No key yet, so VoiceCtrl works offline/)).toBeTruthy();
    fireEvent.click(screen.getByRole("switch", { name: "Start VoiceCtrl with Windows" }));
    click(/Open VoiceCtrl/);
    await settle();

    expect(lastCall(host, "setup.finish")?.args).toEqual({ listening: "online", apiKey: null, startWithWindows: false, smartCleanup: true, provider: "gemini", baseUrl: null, model: null });
  });

  it("downloads the model on the Offline path, and Continue waits for it", async () => {
    const host = await renderSetup({ download: "hold:312" });
    fireEvent.click(screen.getByRole("radio", { name: /Offline/ }));
    click("Continue");
    await settle();
    await settle();

    expect(lastCall(host, "setup.downloadModel")).toBeTruthy();
    expect(screen.getByText("312 of 700 MB · about 40 s left")).toBeTruthy();
    expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("44");
    expect(screen.getByRole("button", { name: "Continue" }).hasAttribute("disabled")).toBe(true);
  });

  it("stops the download on Back and resumes it on the way back in", async () => {
    const host = await renderSetup({ download: "hold:100" });
    fireEvent.click(screen.getByRole("radio", { name: /Offline/ }));
    click("Continue");
    await settle();

    click("Back");
    await settle();
    expect(lastCall(host, "setup.stopDownload")).toBeTruthy();

    click("Continue");
    await settle();
    expect(host.calls.filter((c) => c.name === "setup.downloadModel")).toHaveLength(2);
  });

  it("shows a stopped download with Try again, which resumes it", async () => {
    const host = await renderSetup({ download: "fail:322" });
    fireEvent.click(screen.getByRole("radio", { name: /Offline/ }));
    click("Continue");
    await settle();
    await settle();
    await settle();

    expect(screen.getByRole("alert").textContent).toContain("The download stopped.");
    expect(screen.getByText("322 of 700 MB · stopped")).toBeTruthy();

    click("Try again");
    await settle();
    expect(host.calls.filter((c) => c.name === "setup.downloadModel")).toHaveLength(2);
  });

  it("finishes Offline once the model is in, without a key", async () => {
    const host = await renderSetup({ download: "done" });
    fireEvent.click(screen.getByRole("radio", { name: /Offline/ }));
    click("Continue");
    await settle();

    expect(screen.getByText("Installed · 700 MB · ready to use")).toBeTruthy();
    click("Continue");
    await settle();
    click(/Open VoiceCtrl/);
    await settle();

    expect(lastCall(host, "setup.finish")?.args).toEqual({ listening: "offline", apiKey: null, startWithWindows: true, smartCleanup: true, provider: "gemini", baseUrl: null, model: null });
  });

  it("shows both default holds with a Change button each, and the double-tap as a line", async () => {
    await renderSetup();
    await toReady();

    expect(screen.getByText("Hold Left Ctrl + Left Win to talk")).toBeTruthy();
    expect(screen.getByText("Hold Left Ctrl + Left Shift to rewrite")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Change talk keys" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Change rewrite keys" })).toBeTruthy();
    expect(screen.getByText(/double-tap/i).textContent).toMatch(/Ctrl/);
    // No forced choice: Open VoiceCtrl is there without touching either row.
    expect((screen.getByRole("button", { name: /Open VoiceCtrl/ }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("changes the talk keys through the host and says what the new keys are", async () => {
    const host = await renderSetup();
    await toReady();

    click("Change talk keys");
    await settle();
    expect(screen.getByRole("status").textContent).toBe("Press your combination");
    await press(AltLeft, Space);

    expect(lastCall(host, "settings.captureKey")?.args).toEqual({ target: "triggerKey" });
    expect(screen.getByText("Hold Left Alt + Space to talk")).toBeTruthy();
  });

  it("changes the rewrite keys", async () => {
    const host = await renderSetup();
    await toReady();

    click("Change rewrite keys");
    await settle();
    await press(CtrlLeft, AltLeft);

    expect(lastCall(host, "settings.captureKey")?.args).toEqual({ target: "rewriteKey" });
    expect(screen.getByText("Hold Left Ctrl + Left Alt to rewrite")).toBeTruthy();
  });

  it("warns right there when Windows owns the combination, and keeps listening", async () => {
    await renderSetup();
    await toReady();

    click("Change talk keys");
    await settle();
    await press(MetaLeft, KeyL);

    expect(screen.getByText("Windows uses that combination itself. Choose another.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeTruthy();
  });

  it("warns after saving Ctrl + Shift: Windows may switch the input language", async () => {
    await renderSetup();
    await toReady();

    // Left Ctrl + Left Shift is the rewrite default, so free it first.
    click("Change rewrite keys");
    await settle();
    await press(CtrlLeft, AltLeft);
    click("Change talk keys");
    await settle();
    await press(CtrlLeft, ShiftLeft);

    expect(screen.getByText("Hold Left Ctrl + Left Shift to talk")).toBeTruthy();
    expect(screen.getByText(/Windows may switch the input language/)).toBeTruthy();
  });

  it("refuses the other hold's keys, right there", async () => {
    await renderSetup();
    await toReady();

    click("Change talk keys");
    await settle();
    await press(ShiftLeft, CtrlLeft);

    expect(screen.getByText("That's your Rewrite key. Choose another key.")).toBeTruthy();
    expect(screen.getByText("Hold Left Ctrl + Left Win to talk")).toBeTruthy();
  });

  it("Cancel leaves the keys as they were", async () => {
    await renderSetup();
    await toReady();

    click("Change talk keys");
    await settle();
    click("Cancel");
    await settle();

    expect(screen.getByText("Hold Left Ctrl + Left Win to talk")).toBeTruthy();
  });

  it("drives the window: minimize and close, no maximize on a fixed window", async () => {
    const host = await renderSetup();

    click("Minimize");
    click("Close");

    expect(screen.queryByRole("button", { name: "Maximize" })).toBeNull();
    expect(host.calls.slice(-2).map((c) => c.name)).toEqual(["window.minimize", "window.close"]);
  });
});
