/**
 * A fake host with fixtures (spec §6), so every page runs in plain Chromium: in `npm run dev`,
 * in Vitest and in the Playwright visual tests in CI, with no Windows and no WebView2.
 *
 * It speaks the same envelope as the real host and answers asynchronously, as the real one does.
 */
import type { BridgeTransport, CommandMessage, ResultMessage } from "./bridge";
import type { AppSnapshot, BridgeCommands, BridgeEvents, CommandName, EventName } from "./contracts.g";

type Handler<N extends CommandName> = (
  args: BridgeCommands[N]["args"],
) => BridgeCommands[N]["result"] | Promise<BridgeCommands[N]["result"]>;

/** Throw this from a mock handler to answer `ok: false` with a stable code. */
export class MockHostError extends Error {
  readonly code: string;

  constructor(code: string, message = code) {
    super(message);
    this.code = code;
  }
}

export interface MockHost {
  transport: BridgeTransport;
  /** Every command the page sent, oldest first. */
  readonly calls: CommandMessage[];
  /** Replaces the handler for one command. */
  handle<N extends CommandName>(name: N, handler: Handler<N>): void;
  /** Sends an event to the page, as the host does after the snapshot. */
  emit<E extends EventName>(name: E, payload: BridgeEvents[E]): void;
  snapshot: AppSnapshot;
}

export const defaultSnapshot: AppSnapshot = {
  theme: "light",
  themePreference: "light",
  reducedMotion: false,
  version: "4.0.0",
  status: { kind: "ready", hotkey: "Ctrl", paused: false, offlineFallback: false, talkKeys: null, provider: "Gemini" },
  updatedTo: null,
};

export function createMockHost(snapshot: Partial<AppSnapshot> = {}): MockHost {
  const listeners = new Set<(message: unknown) => void>();
  const handlers = new Map<string, (args: unknown) => unknown>();
  const calls: CommandMessage[] = [];

  const deliver = (message: unknown) => {
    // A macrotask, like WebView2's message pump: never synchronous with the page's post.
    setTimeout(() => {
      for (const listener of listeners) listener(message);
    }, 0);
  };

  const host: MockHost = {
    snapshot: { ...defaultSnapshot, ...snapshot },
    calls,
    transport: {
      post(message) {
        calls.push(message);
        const handler = handlers.get(message.name);
        if (!handler) {
          deliver(fail(message.id, "unknown_command", `No handler for ${message.name}.`));
          return;
        }
        Promise.resolve()
          .then(() => handler(message.args))
          .then(
            (value) => deliver({ t: "res", id: message.id, ok: true, value: value ?? null } satisfies ResultMessage),
            (error: unknown) => {
              const code = error instanceof MockHostError ? error.code : "handler_failed";
              deliver(fail(message.id, code, error instanceof Error ? error.message : String(error)));
            },
          );
      },
      listen(onMessage) {
        listeners.add(onMessage);
        return () => listeners.delete(onMessage);
      },
    },
    handle(name, handler) {
      handlers.set(name, handler as (args: unknown) => unknown);
    },
    emit(name, payload) {
      deliver({ t: "evt", name, payload });
    },
  };

  host.handle("app.ready", () => host.snapshot);
  host.handle("app.setTheme", ({ theme: themePreference }) => {
    // The mock's Windows is set to Light, so System resolves to light.
    const theme = themePreference === "dark" ? "dark" : "light";
    host.snapshot = { ...host.snapshot, theme, themePreference };
    host.emit("app.theme", { theme, themePreference });
    return null;
  });
  host.handle("app.setPaused", ({ paused }) => {
    // Paused shows only while nothing is wrong, as in Core's AppStatusTracker.
    const status = host.snapshot.status;
    const kind = status.kind === "ready" || status.kind === "paused" ? (paused ? "paused" : "ready") : status.kind;
    host.snapshot = { ...host.snapshot, status: { ...status, kind, paused } };
    host.emit("app.status", { status: host.snapshot.status });
    return null;
  });
  host.handle("app.dismissUpdateCard", () => {
    host.snapshot = { ...host.snapshot, updatedTo: null };
    return null;
  });
  let maximized = false;
  host.handle("window.minimize", () => null);
  host.handle("window.toggleMaximize", () => {
    maximized = !maximized;
    host.emit("window.state", { maximized });
    return null;
  });
  host.handle("window.close", () => null);
  return host;
}

function fail(id: number, code: string, message: string): ResultMessage {
  return { t: "res", id, ok: false, error: { code, message } };
}
