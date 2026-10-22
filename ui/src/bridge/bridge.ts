/**
 * The page side of the host bridge (spec §4): one JSON envelope over WebView2's web messages.
 *
 *   page → host  {"t":"cmd","id":7,"name":"dictations.pasteAgain","args":{…}}
 *   host → page  {"t":"res","id":7,"ok":true,"value":…} | {"t":"res","id":7,"ok":false,"error":{…}}
 *   host → page  {"t":"evt","name":"dictation.state","payload":{…}}
 *
 * Pages never touch Windows, files or the network; everything goes through `call` and `on`.
 */
import type { AppSnapshot, BridgeCommands, BridgeEvents, CommandName, EventName } from "./contracts.g";

export interface CommandMessage {
  t: "cmd";
  id: number;
  name: string;
  args: unknown;
}

export interface ResultMessage {
  t: "res";
  id: number;
  ok: boolean;
  value?: unknown;
  error?: { code: string; message: string };
}

export interface EventMessage {
  t: "evt";
  name: string;
  payload: unknown;
}

/** How messages physically travel: WebView2 in the app, the mock host in tests and the gallery. */
export interface BridgeTransport {
  post(message: CommandMessage): void;
  /** Subscribes to host messages; returns the unsubscribe function. */
  listen(onMessage: (message: unknown) => void): () => void;
}

/** A command the host rejected. `code` is stable; `message` is for logs, not for the user. */
export class BridgeError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "BridgeError";
    this.code = code;
  }
}

export interface Bridge {
  call<N extends CommandName>(name: N, args: BridgeCommands[N]["args"]): Promise<BridgeCommands[N]["result"]>;
  on<E extends EventName>(name: E, handler: (payload: BridgeEvents[E]) => void): () => void;
  /** The handshake: the page's first message. Resolves with this surface's full snapshot. */
  ready(): Promise<AppSnapshot>;
  dispose(): void;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isResult(message: Record<string, unknown>): message is Record<string, unknown> & ResultMessage {
  return message.t === "res" && typeof message.id === "number" && typeof message.ok === "boolean";
}

function isEvent(message: Record<string, unknown>): message is Record<string, unknown> & EventMessage {
  return message.t === "evt" && typeof message.name === "string";
}

export function createBridge(transport: BridgeTransport): Bridge {
  let nextId = 1;
  const pending = new Map<number, { resolve: (value: unknown) => void; reject: (error: BridgeError) => void }>();
  const handlers = new Map<string, Set<(payload: unknown) => void>>();

  // The host is trusted, but a malformed or unexpected message must never break the page.
  const stopListening = transport.listen((message) => {
    if (!isObject(message)) return;
    if (isResult(message)) {
      const waiter = pending.get(message.id);
      if (!waiter) return;
      pending.delete(message.id);
      if (message.ok) {
        waiter.resolve(message.value ?? null);
      } else {
        const error: Record<string, unknown> = isObject(message.error) ? message.error : {};
        waiter.reject(new BridgeError(
          typeof error.code === "string" ? error.code : "unknown",
          typeof error.message === "string" ? error.message : "The host rejected the command.",
        ));
      }
    } else if (isEvent(message)) {
      for (const handler of handlers.get(message.name) ?? []) handler(message.payload);
    }
  });

  const bridge: Bridge = {
    call(name, args) {
      const id = nextId++;
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve: resolve as (value: unknown) => void, reject });
        transport.post({ t: "cmd", id, name, args });
      });
    },
    on(name, handler) {
      const set = handlers.get(name) ?? new Set();
      handlers.set(name, set);
      const untyped = handler as (payload: unknown) => void;
      set.add(untyped);
      return () => set.delete(untyped);
    },
    ready() {
      return bridge.call("app.ready", {});
    },
    dispose() {
      stopListening();
      for (const waiter of pending.values()) waiter.reject(new BridgeError("disposed", "The bridge was closed."));
      pending.clear();
      handlers.clear();
    },
  };
  return bridge;
}

interface WebViewHost {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
}

/** The WebView2 transport, or null when the page runs outside the app (browser, tests). */
export function webViewTransport(): BridgeTransport | null {
  const host = (globalThis as { chrome?: { webview?: WebViewHost } }).chrome?.webview;
  if (!host) return null;
  return {
    post: (message) => host.postMessage(message),
    listen(onMessage) {
      const listener = (event: { data: unknown }) => onMessage(event.data);
      host.addEventListener("message", listener);
      return () => host.removeEventListener("message", listener);
    },
  };
}
