import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import type { AppStatus } from "../src/bridge/contracts.g";
import { createMockHost, defaultSnapshot } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import type { HubProps } from "../src/hub/Hub";
import { useHub } from "../src/hub/hubContext";
import { startTargetFromQuery } from "../src/hub/screens";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

async function renderHub(snapshot: Parameters<typeof createMockHost>[0] = {}, renderScreen?: HubProps["renderScreen"]) {
  const host = createMockHost(snapshot);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Hub bridge={bridge} snapshot={ready} {...(renderScreen ? { renderScreen } : {})} />);
  return host;
}

const status = (patch: Partial<AppStatus>): AppStatus => ({ ...defaultSnapshot.status, ...patch });
const nav = () => screen.getByRole("navigation", { name: "Hub" });
const quickFind = () => screen.getByRole("combobox", { name: "Quick Find" });

describe("Hub frame", () => {
  it("always opens on Dictations", async () => {
    await renderHub();

    expect(within(nav()).getByRole("button", { name: "Dictations" }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Dictations");
  });

  it("switches screens from the sidebar", async () => {
    await renderHub();

    fireEvent.click(within(nav()).getByRole("button", { name: "Profiles" }));
    await settle();

    expect(within(nav()).getByRole("button", { name: "Profiles" }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Profiles");
  });

  it("drives the window through window.* and swaps the maximize glyph on window.state", async () => {
    const host = await renderHub();

    fireEvent.click(screen.getByRole("button", { name: "Minimize" }));
    fireEvent.click(screen.getByRole("button", { name: "Maximize" }));
    await settle();
    fireEvent.click(screen.getByRole("button", { name: "Close" }));

    expect(host.calls.slice(-3).map((c) => c.name)).toEqual(["window.minimize", "window.toggleMaximize", "window.close"]);
    expect(screen.getByRole("button", { name: "Restore" })).toBeTruthy();
  });
});

describe("Opening at a place the host asked for", () => {
  it("reads the start screen and row from the address, and ignores anything unknown", () => {
    expect(startTargetFromQuery("?screen=settings&row=api-key")).toEqual({ screen: "settings", row: "api-key" });
    expect(startTargetFromQuery("?screen=settings")).toEqual({ screen: "settings" });
    expect(startTargetFromQuery("?screen=nowhere")).toBeNull();
    expect(startTargetFromQuery("")).toBeNull();
  });

  it("opens on the start screen instead of Dictations", async () => {
    const host = createMockHost();
    const bridge = createBridge(host.transport);
    render(<Hub bridge={bridge} snapshot={await bridge.ready()} renderScreen={() => null} start={{ screen: "settings" }} />);

    expect(within(nav()).getByRole("button", { name: "Settings" }).getAttribute("aria-current")).toBe("page");
  });

  it("moves an open Hub on hub.show", async () => {
    const host = await renderHub({}, () => null);

    host.emit("hub.show", { screen: "settings", row: "api-key" });
    await settle();

    expect(within(nav()).getByRole("button", { name: "Settings" }).getAttribute("aria-current")).toBe("page");
  });
});

describe("Theme", () => {
  it("puts the snapshot's Theme on <html> and follows app.theme", async () => {
    const host = await renderHub({ theme: "dark", themePreference: "dark" });
    expect(document.documentElement.dataset.theme).toBe("dark");

    host.emit("app.theme", { theme: "light", themePreference: "system" });
    await settle();

    expect(document.documentElement.dataset.theme).toBe("light");
  });

  it("the status card toggle switches to the other Theme", async () => {
    const host = await renderHub();

    fireEvent.click(screen.getByRole("button", { name: "Switch to Dark" }));
    await settle();

    expect(host.calls.at(-1)).toMatchObject({ name: "app.setTheme", args: { theme: "dark" } });
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(screen.getByRole("button", { name: "Switch to Light" })).toBeTruthy();
  });

  it("the Settings Theme row offers System, which the host resolves", async () => {
    const host = await renderHub();
    fireEvent.click(within(nav()).getByRole("button", { name: "Settings" }));
    await settle();

    fireEvent.click(screen.getByRole("radio", { name: "System" }));
    await settle();

    expect(host.calls.at(-1)).toMatchObject({ name: "app.setTheme", args: { theme: "system" } });
    expect(screen.getByRole("radio", { name: "System" }).getAttribute("aria-checked")).toBe("true");
  });

  it("follows app.reducedMotion", async () => {
    const host = await renderHub();

    host.emit("app.reducedMotion", { reducedMotion: true });
    await settle();

    expect(document.documentElement.dataset.reducedMotion).toBe("true");
  });
});

describe("Quick Find", () => {
  it("shows nothing until something is typed", async () => {
    await renderHub();

    fireEvent.focus(quickFind());

    expect(screen.queryByRole("listbox")).toBeNull();
    expect(quickFind().getAttribute("aria-expanded")).toBe("false");
  });

  it("lists matches with where they live, and says when nothing matches", async () => {
    await renderHub();
    fireEvent.focus(quickFind());

    fireEvent.change(quickFind(), { target: { value: "dark" } });
    const option = within(screen.getByRole("listbox")).getByRole("option");
    expect(option.textContent).toBe("ThemeSettings");

    fireEvent.change(quickFind(), { target: { value: "zzz" } });
    expect(screen.getByRole("listbox").textContent).toBe("No screen or setting by that name");
  });

  it("Enter opens Settings, lands on the row and flashes it, then the flash ends", async () => {
    // Faked only after the render: the mock host answers the handshake on a real timer.
    await renderHub();
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "requestAnimationFrame"] });
    try {
      fireEvent.focus(quickFind());
      fireEvent.change(quickFind(), { target: { value: "dark mode" } });

      fireEvent.keyDown(quickFind(), { key: "Enter" });
      await act(() => vi.advanceTimersByTimeAsync(50));

      expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Settings");
      const row = document.getElementById("find-theme");
      expect(row?.classList.contains("vc-setting-flash")).toBe(true);
      expect((quickFind() as HTMLInputElement).value).toBe("");

      await act(() => vi.advanceTimersByTimeAsync(1700));
      expect(row?.classList.contains("vc-setting-flash")).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it("arrow keys move the highlight, and Enter picks it", async () => {
    await renderHub();
    fireEvent.focus(quickFind());
    fireEvent.change(quickFind(), { target: { value: "di" } });
    const options = within(screen.getByRole("listbox")).getAllByRole("option");
    expect(options[0]?.getAttribute("aria-selected")).toBe("true");

    fireEvent.keyDown(quickFind(), { key: "ArrowDown" });
    expect(quickFind().getAttribute("aria-activedescendant")).toBe(options[1]?.id);
    const second = options[1]?.textContent;

    fireEvent.keyDown(quickFind(), { key: "Enter" });
    await settle();

    expect(second).toBe("DictionaryScreen");
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Dictionary");
  });

  it("Esc clears the box and closes the results", async () => {
    await renderHub();
    fireEvent.focus(quickFind());
    fireEvent.change(quickFind(), { target: { value: "mic" } });

    fireEvent.keyDown(quickFind(), { key: "Escape" });

    expect((quickFind() as HTMLInputElement).value).toBe("");
    expect(quickFind().getAttribute("aria-expanded")).toBe("false");
  });

  it("a click on a result goes there", async () => {
    await renderHub();
    fireEvent.focus(quickFind());
    fireEvent.change(quickFind(), { target: { value: "snip" } });

    fireEvent.mouseDown(within(screen.getByRole("listbox")).getAllByRole("option")[0] as HTMLElement);
    await settle();

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Snippets");
  });
});

describe("Status card", () => {
  it("Ready teaches the talk keys when the Trigger Key is on", async () => {
    await renderHub({ status: status({ talkKeys: ["Left Ctrl", "Left Win"] }) });

    expect(screen.getByText("Hold Left Ctrl + Left Win to talk")).toBeTruthy();
  });

  it("Ready teaches the Hotkey", async () => {
    await renderHub({ status: status({ hotkey: "Right Ctrl" }) });

    expect(screen.getByText("Ready")).toBeTruthy();
    expect(screen.getByText("Double-tap Right Ctrl to show the bar")).toBeTruthy();
  });

  it("No microphone links to the microphone row", async () => {
    await renderHub({ status: status({ kind: "noMicrophone" }) });

    fireEvent.click(screen.getByRole("button", { name: /No microphone/ }));
    await settle();

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Settings");
    expect(document.getElementById("find-microphone")?.classList.contains("vc-setting-flash")).toBe(true);
  });

  it.each([
    [true, "Using Offline until it's fixed"],
    [false, "Fix it in Settings"],
  ])("Gemini key not working says whether Offline stands in (%s)", async (offlineFallback, hint) => {
    await renderHub({ status: status({ kind: "apiKeyNotWorking", offlineFallback }) });

    fireEvent.click(screen.getByRole("button", { name: new RegExp(`Gemini key not working.*${hint}`) }));
    await settle();

    expect(document.getElementById("find-api-key")?.classList.contains("vc-setting-flash")).toBe(true);
  });

  it("Paused offers Resume, which unpauses through the host", async () => {
    const host = await renderHub({ status: status({ kind: "paused", paused: true }) });

    fireEvent.click(screen.getByRole("button", { name: /Paused.*Resume/ }));
    await settle();

    expect(host.calls.at(-1)).toMatchObject({ name: "app.setPaused", args: { paused: false } });
    expect(screen.getByText("Ready")).toBeTruthy();
  });

  it("follows app.status", async () => {
    const host = await renderHub();

    host.emit("app.status", { status: status({ kind: "noMicrophone" }) });
    await settle();

    expect(screen.getByText("No microphone")).toBeTruthy();
  });
});

describe("Toast and dialog", () => {
  function Probe() {
    const hub = useHub();
    return (
      <div>
        <button type="button" onClick={() => hub.toast("Dictation deleted", { action: { label: "Undo", onClick: () => hub.toast("Restored") } })}>
          toast
        </button>
        <button
          type="button"
          onClick={() =>
            void hub
              .confirm({ title: "Clear all Dictations?", body: "It can't be undone.", confirmLabel: "Clear all" })
              .then((ok) => hub.toast(ok ? "Cleared" : "Kept"))
          }
        >
          confirm
        </button>
      </div>
    );
  }

  it("a toast carries its action and a new toast replaces it", async () => {
    await renderHub({}, () => <Probe />);

    fireEvent.click(screen.getByRole("button", { name: "toast" }));
    expect(screen.getByRole("status").textContent).toContain("Dictation deleted");

    fireEvent.click(screen.getByRole("button", { name: "Undo" }));
    await settle();

    expect(screen.getByRole("status").textContent).toContain("Restored");
  });

  it("confirm resolves with the choice, Cancel focused by default", async () => {
    await renderHub({}, () => <Probe />);

    fireEvent.click(screen.getByRole("button", { name: "confirm" }));
    await settle();
    const dialog = screen.getByRole("dialog", { name: "Clear all Dictations?" });
    expect(document.activeElement?.textContent).toBe("Cancel");

    fireEvent.click(within(dialog).getByRole("button", { name: "Clear all" }));
    await settle();

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByRole("status").textContent).toContain("Cleared");
  });
});

describe("The Updated to card", () => {
  const card = () => screen.queryByRole("region", { name: "Updated to 4.1.0" });

  it("isn't there without an update", async () => {
    await renderHub();

    expect(screen.queryByText(/Updated to/)).toBeNull();
  });

  it("shows the version above the status card after an update", async () => {
    await renderHub({ updatedTo: "4.1.0" });

    expect(card()).toBeTruthy();
    expect(within(card()!).getByText("Installed when VoiceCtrl started")).toBeTruthy();
  });

  it("goes for good on ✕", async () => {
    const host = await renderHub({ updatedTo: "4.1.0" });

    fireEvent.click(within(card()!).getByRole("button", { name: "Dismiss" }));
    await settle();

    expect(card()).toBeNull();
    expect(host.calls.at(-1)?.name).toBe("app.dismissUpdateCard");
  });

  it("opens the release notes on See what's new, and goes", async () => {
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    const host = await renderHub({ updatedTo: "4.1.0" });

    fireEvent.click(within(card()!).getByRole("button", { name: /See what's new/ }));
    await settle();

    expect(open).toHaveBeenCalledWith("https://github.com/aadershk/VoiceCtrl/releases/tag/v4.1.0", "_blank", "noopener");
    expect(card()).toBeNull();
    expect(host.calls.at(-1)?.name).toBe("app.dismissUpdateCard");
    open.mockRestore();
  });
});
