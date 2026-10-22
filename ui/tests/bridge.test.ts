import { describe, expect, it } from "vitest";
import { BridgeError, createBridge } from "../src/bridge/bridge";
import type { BridgeTransport } from "../src/bridge/bridge";
import { createMockHost, MockHostError } from "../src/bridge/mock";

/** A transport the test drives by hand, to feed the page raw (and broken) host messages. */
function manualTransport() {
  const posted: unknown[] = [];
  let listener: (message: unknown) => void = () => undefined;
  const transport: BridgeTransport = {
    post: (message) => posted.push(message),
    listen(onMessage) {
      listener = onMessage;
      return () => {
        listener = () => undefined;
      };
    },
  };
  return { transport, posted, send: (message: unknown) => listener(message) };
}

describe("bridge envelope", () => {
  it("sends a command in the §4 envelope and resolves with the matching result", async () => {
    const wire = manualTransport();
    const bridge = createBridge(wire.transport);
    const result = bridge.call("app.setTheme", { theme: "dark" });
    expect(wire.posted).toEqual([{ t: "cmd", id: 1, name: "app.setTheme", args: { theme: "dark" } }]);
    wire.send({ t: "res", id: 1, ok: true, value: null });
    await expect(result).resolves.toBeNull();
  });

  it("matches results to calls by id, whatever order they come back in", async () => {
    const wire = manualTransport();
    const bridge = createBridge(wire.transport);
    const first = bridge.call("app.ready", {});
    const second = bridge.call("window.close", {});
    wire.send({ t: "res", id: 2, ok: true, value: null });
    wire.send({ t: "res", id: 1, ok: true, value: { theme: "dark", themePreference: "light", reducedMotion: false, version: "4.0.0" } });
    await expect(second).resolves.toBeNull();
    await expect(first).resolves.toEqual({ theme: "dark", themePreference: "light", reducedMotion: false, version: "4.0.0" });
  });

  it("rejects with a BridgeError carrying the host's code", async () => {
    const wire = manualTransport();
    const bridge = createBridge(wire.transport);
    const result = bridge.call("window.close", {});
    wire.send({ t: "res", id: 1, ok: false, error: { code: "not_allowed", message: "No." } });
    await expect(result).rejects.toMatchObject({ name: "BridgeError", code: "not_allowed" });
  });

  it("ignores malformed and unknown messages instead of throwing", async () => {
    const wire = manualTransport();
    const bridge = createBridge(wire.transport);
    const result = bridge.call("window.minimize", {});
    for (const junk of [null, 42, "res", { t: "res" }, { t: "res", id: 99, ok: true }, { t: "evt" }, { t: "other" }]) {
      expect(() => wire.send(junk)).not.toThrow();
    }
    wire.send({ t: "res", id: 1, ok: true });
    await expect(result).resolves.toBeNull();
  });

  it("delivers events to subscribers until they unsubscribe", () => {
    const wire = manualTransport();
    const bridge = createBridge(wire.transport);
    const seen: string[] = [];
    const stop = bridge.on("app.theme", ({ theme }) => seen.push(theme));
    wire.send({ t: "evt", name: "app.theme", payload: { theme: "dark" } });
    stop();
    wire.send({ t: "evt", name: "app.theme", payload: { theme: "light" } });
    expect(seen).toEqual(["dark"]);
  });

  it("rejects pending calls when disposed", async () => {
    const wire = manualTransport();
    const bridge = createBridge(wire.transport);
    const result = bridge.call("app.ready", {});
    bridge.dispose();
    await expect(result).rejects.toBeInstanceOf(BridgeError);
  });
});

describe("mock host", () => {
  it("answers the app.ready handshake with its snapshot", async () => {
    const host = createMockHost({ theme: "dark" });
    const bridge = createBridge(host.transport);
    await expect(bridge.ready()).resolves.toEqual({
      theme: "dark",
      themePreference: "light",
      reducedMotion: false,
      version: "4.0.0",
      status: { kind: "ready", hotkey: "Ctrl", paused: false, offlineFallback: false, talkKeys: null, provider: "Gemini" },
      updatedTo: null,
    });
    expect(host.calls.map((c) => c.name)).toEqual(["app.ready"]);
  });

  it("answers asynchronously, never inside the page's post", () => {
    const host = createMockHost();
    let answered = false;
    host.transport.listen(() => {
      answered = true;
    });
    host.transport.post({ t: "cmd", id: 1, name: "app.ready", args: {} });
    expect(answered).toBe(false);
  });

  it("emits app.theme after app.setTheme, as the real host does", async () => {
    const host = createMockHost();
    const bridge = createBridge(host.transport);
    const themes: string[] = [];
    bridge.on("app.theme", ({ theme }) => themes.push(theme));
    await bridge.call("app.setTheme", { theme: "dark" });
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(themes).toEqual(["dark"]);
    expect(host.snapshot.theme).toBe("dark");
  });

  it("turns handler errors and missing handlers into ok:false results", async () => {
    const host = createMockHost();
    const bridge = createBridge(host.transport);
    host.handle("window.close", () => {
      throw new MockHostError("busy", "Still saving.");
    });
    await expect(bridge.call("window.close", {})).rejects.toMatchObject({ code: "busy" });
    // A command the mock doesn't know, as a page from a newer slice might send.
    const untyped = bridge as unknown as { call(name: string, args: unknown): Promise<unknown> };
    await expect(untyped.call("nope.missing", {})).rejects.toMatchObject({ code: "unknown_command" });
  });
});
